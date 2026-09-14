using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Lists the ONNX GenAI variants of a Hugging Face repository (top-level folders containing genai_config.json) and installs one
/// into the models directory. The listing is pinned to one commit and every file is verified against the hash published by the hub.
/// </summary>
public sealed class HuggingFaceModelSource : IRemoteModelSource, IDisposable
{
    public const string DefaultRepository = "esilva/SlopCoder-Mongo-0.5B-ONNX";
    private static readonly Uri DefaultBaseAddress = new("https://huggingface.co/");
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private const long ProgressIntervalMilliseconds = 100;
    private readonly string _repository;
    private readonly Uri _baseAddress;
    private readonly HttpClient _http;

    public HuggingFaceModelSource(string repository = DefaultRepository, HttpMessageHandler? handler = null, Uri? baseAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        _repository = repository;
        _baseAddress = baseAddress ?? DefaultBaseAddress;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        // Per-request limits: a timeout for the listing, a stall timeout for large files.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EsilvaSoft.SlopStudio", "1"));
    }

    public Uri RepositoryUrl => new(_baseAddress, _repository);

    public async Task<IReadOnlyList<RemoteModelVariant>> ListAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ListTimeout);
        try
        {
            using var info = await GetJsonAsync($"api/models/{_repository}", timeout.Token);
            var revision = TryString(info.RootElement, "sha");
            if (!IsHex(revision, 40)) throw new InvalidDataException("O repositório não informou uma revisão válida.");
            var license = info.RootElement.TryGetProperty("cardData", out var card) && card.ValueKind == JsonValueKind.Object
                ? TryString(card, "license_name") ?? TryString(card, "license") : null;
            using var tree = await GetJsonAsync($"api/models/{_repository}/tree/{revision}?recursive=true", timeout.Token);
            return BuildVariants(tree.RootElement, revision!, license);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("O Hugging Face não respondeu a tempo.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Resposta inesperada do Hugging Face.", ex);
        }
    }

    public async Task<string> DownloadAsync(RemoteModelVariant variant, string modelsDirectory, IProgress<RemoteModelProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsDirectory);
        if (!IsHex(variant.Revision, 40) || !IsSafeRelativePath(variant.Variant) || !IsSafeRelativePath(variant.FolderName) || variant.FolderName.Contains('/')
            || variant.Files.Any(file => !file.Path.StartsWith(variant.Variant + "/", StringComparison.Ordinal) || !IsSafeRelativePath(file.Path)))
            throw new InvalidDataException("Descrição de modelo inválida.");
        var root = Path.GetFullPath(modelsDirectory);
        var target = Path.Combine(root, variant.FolderName);
        if (Directory.Exists(target)) throw new IOException($"O modelo já está instalado em {target}. Remova a pasta para baixar novamente.");
        // Dot-prefixed, so the catalog ignores it; verified files survive a failed or cancelled attempt and the next one resumes.
        var staging = Path.Combine(root, "." + variant.FolderName + ".download");
        Directory.CreateDirectory(staging);
        EnsureFreeSpace(root, staging, variant.SizeBytes);
        var reporter = new ThrottledProgress(progress, variant.SizeBytes);
        long completed = 0;
        foreach (var file in variant.Files)
        {
            var relative = file.Path[(variant.Variant.Length + 1)..];
            var destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination) && new FileInfo(destination).Length == file.Size
                && string.Equals(await HashFileAsync(destination, file, cancellationToken), Expected(file), StringComparison.Ordinal))
            {
                completed += file.Size;
                reporter.Report(completed, relative, force: true);
                continue;
            }
            File.Delete(destination);
            completed = await DownloadFileAsync(variant, file, destination, completed, relative, reporter, cancellationToken);
        }
        Directory.Move(staging, target);
        reporter.Report(variant.SizeBytes, "", force: true);
        return target;
    }

    public void Dispose() => _http.Dispose();

    private RemoteModelVariant[] BuildVariants(JsonElement tree, string revision, string? license)
    {
        var files = new List<(string Variant, RemoteModelFile File)>();
        foreach (var entry in tree.EnumerateArray())
        {
            if (TryString(entry, "type") != "file" || TryString(entry, "path") is not { } path) continue;
            var separator = path.IndexOf('/');
            // Root files (README, licenses) are not models; unsafe paths could never be written inside the models directory.
            if (separator <= 0 || !IsSafeRelativePath(path)) continue;
            var oid = TryString(entry, "oid");
            var sha256 = entry.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object ? TryString(lfs, "oid") : null;
            if (!IsHex(oid, 40) || sha256 is not null && !IsHex(sha256, 64)) throw new InvalidDataException($"Hash inválido para {path}.");
            var size = entry.GetProperty("size").GetInt64();
            if (size < 0) throw new InvalidDataException($"Tamanho inválido para {path}.");
            files.Add((path[..separator], new RemoteModelFile(path, size, sha256?.ToLowerInvariant(), oid!.ToLowerInvariant())));
        }
        var repositoryName = _repository[(_repository.LastIndexOf('/') + 1)..];
        return files.GroupBy(item => item.Variant, StringComparer.Ordinal)
            .Where(group => group.Any(item => item.File.Path == group.Key + "/genai_config.json"))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new RemoteModelVariant(_repository, revision, group.Key, $"{repositoryName}-{group.Key}", group.Sum(item => item.File.Size),
                license, new Uri($"{RepositoryUrl.AbsoluteUri}/tree/{revision}/{Uri.EscapeDataString(group.Key)}"), group.Select(item => item.File).ToArray()))
            .ToArray();
    }

    private async Task<JsonDocument> GetJsonAsync(string relative, CancellationToken token)
    {
        using var response = await _http.GetAsync(new Uri(_baseAddress, relative), HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }

    private async Task<long> DownloadFileAsync(RemoteModelVariant variant, RemoteModelFile file, string destination, long completed, string relative,
        ThrottledProgress reporter, CancellationToken token)
    {
        var part = destination + ".part";
        try
        {
            var address = new Uri(_baseAddress, $"{_repository}/resolve/{variant.Revision}/{string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString))}");
            using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var hash = CreateHash(file);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
            var buffer = new byte[81920];
            long received = 0;
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.Asynchronous))
            {
                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    int read;
                    try { read = await source.ReadAsync(buffer, stall.Token); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        throw new TimeoutException($"O download de {relative} parou de responder.");
                    }
                    if (read == 0) break;
                    received += read;
                    if (received > file.Size) throw new InvalidDataException($"{relative} é maior que o tamanho publicado e foi descartado.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    reporter.Report(completed + received, relative);
                }
            }
            if (received != file.Size || !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), Expected(file), StringComparison.Ordinal))
                throw new InvalidDataException($"{relative} não confere com o hash publicado no repositório e foi descartado.");
            File.Move(part, destination);
            return completed + received;
        }
        catch
        {
            try { File.Delete(part); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private static async Task<string> HashFileAsync(string path, RemoteModelFile file, CancellationToken token)
    {
        using var hash = CreateHash(file);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0) hash.AppendData(buffer, 0, read);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>LFS files are identified by the SHA-256 of their content; regular git files by SHA-1 over "blob &lt;size&gt;\0" + content.</summary>
    private static IncrementalHash CreateHash(RemoteModelFile file)
    {
        if (file.Sha256 is not null) return IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"blob {file.Size}\0")));
        return hash;
    }

    private static string Expected(RemoteModelFile file) => file.Sha256 ?? file.GitBlobSha1;

    private static void EnsureFreeSpace(string root, string staging, long totalBytes)
    {
        var required = totalBytes - Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        long available;
        try { available = new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return; }
        if (available < required)
            throw new IOException(string.Create(CultureInfo.CurrentCulture, $"Espaço insuficiente em {root}: são necessários {required / 1_000_000:N0} MB."));
    }

    private static bool IsSafeRelativePath(string path) =>
        path.Length > 0 && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or "..") && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

    private static bool IsHex(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);

    private static string? TryString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class ThrottledProgress(IProgress<RemoteModelProgress>? progress, long total)
    {
        private long _lastReport = -1;

        public void Report(long completed, string file, bool force = false)
        {
            if (progress is null) return;
            var now = Environment.TickCount64;
            if (!force && _lastReport >= 0 && now - _lastReport < ProgressIntervalMilliseconds) return;
            _lastReport = now;
            progress.Report(new RemoteModelProgress(completed, total, file));
        }
    }
}
