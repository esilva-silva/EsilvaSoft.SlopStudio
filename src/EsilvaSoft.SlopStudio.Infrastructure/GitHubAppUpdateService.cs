using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Where the running installation lives and whether it may replace itself.</summary>
public sealed record AppUpdateOptions(AppUpdateAvailability Availability, AppVersion CurrentVersion, string Rid, string TargetDirectory,
    string ExecutableName, string UpdatesDirectory, Uri ReleasesApi)
{
    public static Uri GitHubReleasesApi { get; } = new("https://api.github.com/repos/esilva-silva/EsilvaSoft.SlopStudio/releases?per_page=20");

    public static AppUpdateOptions FromProcess()
    {
        var processPath = Environment.ProcessPath;
        var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = AppVersion.TryParse(informational, out var parsed) ? parsed : AppVersion.Parse("0.0.0-local");
        var rid = GitHubAppUpdateService.CurrentRid();
        return new(GitHubAppUpdateService.DetectAvailability(processPath, version, rid), version, rid ?? "",
            processPath is null ? AppContext.BaseDirectory : Path.GetDirectoryName(processPath)!,
            processPath is null ? "" : Path.GetFileName(processPath), LocalWorkspacePaths.GetUpdatesDirectory(), GitHubReleasesApi);
    }
}

internal sealed record PendingAppUpdate(string Version, string PayloadDirectory, string TargetDirectory, string ExecutableName, string? LastError = null);

/// <summary>
/// Checks GitHub Releases, stages a SHA-256 verified package and swaps the files by rename after the application exits.
/// Windows allows renaming a running executable but not overwriting it; on Linux the running process keeps the old inode.
/// </summary>
public sealed class GitHubAppUpdateService : IAppUpdateService, IDisposable
{
    public const string ExecutableBaseName = "EsilvaSoft.SlopStudio.Desktop";
    internal const string PendingFileName = "pending.json";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private const UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private static readonly JsonSerializerOptions ApiJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly JsonSerializerOptions PendingJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly AppUpdateOptions _options;
    private readonly HttpClient _http;

    public GitHubAppUpdateService(AppUpdateOptions options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        // Per-request limits: a short timeout for the feed, a stall timeout for the package.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EsilvaSoft.SlopStudio", options.CurrentVersion.ToString()));
    }

    public AppUpdateAvailability Availability => _options.Availability;
    public AppVersion CurrentVersion => _options.CurrentVersion;

    public StagedAppUpdate? GetStagedUpdate() =>
        Availability == AppUpdateAvailability.Disabled || ReadValidPending(_options) is not { } pending
            ? null
            : new StagedAppUpdate(AppVersion.Parse(pending.Version), pending.LastError);

    public async Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        if (Availability == AppUpdateAvailability.Disabled) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.ReleasesApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            // Rate limits (403/429) and outages are not user errors: the next scheduled check retries.
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, ApiJson, timeout.Token) ?? [];
            return AppUpdateSelector.SelectNewest(CurrentVersion, releases.Select(ToCandidate).OfType<AppReleaseCandidate>(), _options.Rid);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException) { return null; }
    }

    public async Task<StagedAppUpdate> DownloadAsync(AppUpdateRelease release, ApplicationOperationScope operation)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(operation);
        if (Availability != AppUpdateAvailability.Supported) throw new InvalidOperationException("A atualização automática não está disponível nesta instalação.");
        var token = operation.Token;
        var versionDirectory = Path.Combine(_options.UpdatesDirectory, release.Version.ToString());
        var pendingPath = Path.Combine(_options.UpdatesDirectory, PendingFileName);
        if (ReadValidPending(_options) is { } previous && AppVersion.Parse(previous.Version) == release.Version) TryDelete(pendingPath);
        TryDeleteDirectory(versionDirectory);
        Directory.CreateDirectory(versionDirectory);
        var package = Path.Combine(versionDirectory, release.AssetName);
        try
        {
            var expected = release.Sha256 ?? await DownloadChecksumAsync(release, token)
                ?? throw new InvalidDataException("O release não publica SHA-256 para este pacote; a atualização não foi baixada.");
            var actual = await DownloadPackageAsync(release, package + ".partial", operation);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O pacote baixado não confere com o SHA-256 publicado e foi descartado.");
            File.Move(package + ".partial", package);
            var payload = Path.Combine(versionDirectory, "payload");
            Extract(package, payload);
            if (!File.Exists(Path.Combine(payload, _options.ExecutableName))) throw new InvalidDataException("O pacote não contém o executável esperado.");
            File.Delete(package);
            WritePending(_options.UpdatesDirectory, new PendingAppUpdate(release.Version.ToString(), payload, _options.TargetDirectory, _options.ExecutableName));
        }
        catch
        {
            TryDeleteDirectory(versionDirectory);
            throw;
        }
        foreach (var other in Directory.EnumerateDirectories(_options.UpdatesDirectory))
            if (!SamePath(other, versionDirectory)) TryDeleteDirectory(other);
        return new StagedAppUpdate(release.Version, null);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Entry point hook after the UI lifetime ended. Exiting must never fail because of an update.</summary>
    public static void ApplyPendingOnExit(bool restart)
    {
        try
        {
            var options = AppUpdateOptions.FromProcess();
            if (options.Availability != AppUpdateAvailability.Supported) return;
            ApplyPending(options);
            if (restart)
                Process.Start(new ProcessStartInfo(Path.Combine(options.TargetDirectory, options.ExecutableName))
                    { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The failure is recorded in pending.json when it happened during the swap; nothing else can be reported after exit.
        }
    }

    /// <summary>Removes leftovers of a previous swap and staging folders no longer referenced.</summary>
    public static void CleanupAfterStart()
    {
        try
        {
            var options = AppUpdateOptions.FromProcess();
            if (options.Availability == AppUpdateAvailability.Supported) Cleanup(options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static bool ApplyPending(AppUpdateOptions options)
    {
        PendingAppUpdate? pending;
        bool valid;
        try
        {
            // Exclusive handle: a second instance closing at the same time skips instead of racing the renames.
            using var stream = new FileStream(Path.Combine(options.UpdatesDirectory, PendingFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            pending = Deserialize(stream);
            valid = pending is not null && IsValid(pending, options);
            if (valid && pending is not null)
            {
                try { ReplaceFiles(pending.PayloadDirectory, options.TargetDirectory, pending.ExecutableName); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    stream.SetLength(0);
                    JsonSerializer.Serialize(stream, pending with { LastError = ex.Message }, PendingJson);
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        Discard(options, pending);
        return valid;
    }

    internal static void ReplaceFiles(string payloadDirectory, string targetDirectory, string executableName)
    {
        // Other files first; swapping the executable is the committing step.
        var files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(payloadDirectory, file))
            .OrderBy(relative => string.Equals(relative, executableName, PathComparison) ? 1 : 0)
            .ThenBy(relative => relative, StringComparer.Ordinal)
            .ToArray();
        if (!files.Contains(executableName, PathComparer)) throw new InvalidDataException("A atualização preparada não contém o executável.");
        var replaced = new List<(string Target, string? Backup)>();
        try
        {
            foreach (var relative in files)
            {
                var target = Path.Combine(targetDirectory, relative);
                var staged = target + ".new";
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(payloadDirectory, relative), staged, overwrite: true);
                if (!OperatingSystem.IsWindows() && string.Equals(relative, executableName, StringComparison.Ordinal))
                    File.SetUnixFileMode(staged, ExecutableMode);
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = FreeBackupPath(target);
                    File.Move(target, backup);
                }
                replaced.Add((target, backup));
                File.Move(staged, target);
            }
        }
        catch
        {
            for (var index = replaced.Count - 1; index >= 0; index--)
            {
                var (target, backup) = replaced[index];
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (backup is not null) File.Move(backup, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (var relative in files) TryDelete(Path.Combine(targetDirectory, relative) + ".new");
            throw;
        }
    }

    internal static void Cleanup(AppUpdateOptions options)
    {
        foreach (var file in Directory.EnumerateFiles(options.TargetDirectory, ExecutableBaseName + "*"))
            if (file.EndsWith(".old", StringComparison.Ordinal) || file.EndsWith(".new", StringComparison.Ordinal)) TryDelete(file);
        if (!Directory.Exists(options.UpdatesDirectory)) return;
        var pending = ReadValidPending(options);
        if (pending is null) TryDelete(Path.Combine(options.UpdatesDirectory, PendingFileName));
        var keep = pending is null ? null : Path.GetDirectoryName(Path.GetFullPath(pending.PayloadDirectory));
        foreach (var directory in Directory.EnumerateDirectories(options.UpdatesDirectory))
            if (keep is null || !SamePath(directory, keep)) TryDeleteDirectory(directory);
    }

    internal static void WritePending(string updatesDirectory, PendingAppUpdate pending)
    {
        Directory.CreateDirectory(updatesDirectory);
        var path = Path.Combine(updatesDirectory, PendingFileName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(pending, PendingJson));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    internal static AppUpdateAvailability DetectAvailability(string? processPath, AppVersion version, string? rid)
    {
        if (Environment.GetEnvironmentVariable("SLOPSTUDIO_DISABLE_UPDATES") is "1" or "true" || rid is null || processPath is null
            || version is { Major: 0, Minor: 0, Patch: 0 }) return AppUpdateAvailability.Disabled;
        var directory = Path.GetDirectoryName(processPath)!;
        // Only the published single-file executable replaces itself; build output keeps the assembly next to the host.
        if (!string.Equals(Path.GetFileNameWithoutExtension(processPath), ExecutableBaseName, StringComparison.Ordinal)
            || File.Exists(Path.Combine(directory, ExecutableBaseName + ".dll"))) return AppUpdateAvailability.Disabled;
        return CanWrite(directory) ? AppUpdateAvailability.Supported : AppUpdateAvailability.ManualOnly;
    }

    internal static string? CurrentRid()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : null;
        var architecture = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => null };
        return os is null || architecture is null ? null : $"{os}-{architecture}";
    }

    private async Task<string?> DownloadChecksumAsync(AppUpdateRelease release, CancellationToken token) =>
        release.ChecksumsUrl is null ? null : AppUpdateSelector.FindChecksum(await _http.GetStringAsync(release.ChecksumsUrl, token), release.AssetName);

    private async Task<string> DownloadPackageAsync(AppUpdateRelease release, string destination, ApplicationOperationScope operation)
    {
        var token = operation.Token;
        using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? release.Size;
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
        var buffer = new byte[81920];
        long completed = 0;
        try
        {
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                var read = await source.ReadAsync(buffer, stall.Token);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                completed += read;
                operation.Report(completed, total);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("O download da atualização parou de responder.");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Extract(string package, string destination)
    {
        Directory.CreateDirectory(destination);
        if (package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(package, destination);
            return;
        }
        using var file = File.OpenRead(package);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: false);
    }

    private static AppReleaseCandidate? ToCandidate(GitHubRelease release)
    {
        if (release.TagName is null || !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var page)) return null;
        var assets = new List<AppReleaseAsset>();
        foreach (var asset in release.Assets ?? [])
            if (asset.Name is not null && Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var download))
                assets.Add(new AppReleaseAsset(asset.Name, download, asset.Size, asset.Digest));
        return new AppReleaseCandidate(release.TagName, release.Draft, release.Prerelease, page, assets);
    }

    private static PendingAppUpdate? ReadValidPending(AppUpdateOptions options)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(options.UpdatesDirectory, PendingFileName), FileMode.Open, FileAccess.Read, FileShare.Read);
            return Deserialize(stream) is { } pending && IsValid(pending, options) ? pending : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static PendingAppUpdate? Deserialize(Stream stream)
    {
        try { return JsonSerializer.Deserialize<PendingAppUpdate>(stream, PendingJson); }
        catch (JsonException) { return null; }
    }

    private static bool IsValid(PendingAppUpdate pending, AppUpdateOptions options) =>
        pending is { Version: not null, PayloadDirectory: not null, TargetDirectory: not null, ExecutableName: not null }
        && AppVersion.TryParse(pending.Version, out var version) && version > options.CurrentVersion
        && SamePath(pending.TargetDirectory, options.TargetDirectory)
        && IsUnder(pending.PayloadDirectory, options.UpdatesDirectory)
        && string.Equals(pending.ExecutableName, options.ExecutableName, PathComparison)
        && File.Exists(Path.Combine(pending.PayloadDirectory, pending.ExecutableName));

    private static void Discard(AppUpdateOptions options, PendingAppUpdate? pending)
    {
        TryDelete(Path.Combine(options.UpdatesDirectory, PendingFileName));
        if (pending?.PayloadDirectory is { } payload && Path.GetDirectoryName(Path.GetFullPath(payload)) is { } versionDirectory
            && IsUnder(versionDirectory, options.UpdatesDirectory)) TryDeleteDirectory(versionDirectory);
    }

    private static string FreeBackupPath(string target)
    {
        var backup = target + ".old";
        try
        {
            File.Delete(backup);
            return backup;
        }
        // A previous instance that is still exiting keeps its renamed image locked.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"{target}.{Guid.NewGuid():N}.old"; }
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            using (new FileStream(Path.Combine(directory, $".slopstudio-write-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool SamePath(string left, string right) => string.Equals(NormalizePath(left), NormalizePath(right), PathComparison);
    private static bool IsUnder(string child, string parent) => NormalizePath(child).StartsWith(NormalizePath(parent) + Path.DirectorySeparatorChar, PathComparison);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record GitHubRelease(string? TagName, bool Draft, bool Prerelease, string? HtmlUrl, GitHubAsset[]? Assets);
    private sealed record GitHubAsset(string? Name, long Size, string? BrowserDownloadUrl, string? Digest);
}
