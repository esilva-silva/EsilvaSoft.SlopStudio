using System.Text;
using EsilvaSoft.SlopStudio.Application;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class LocalScriptFileService : IScriptFileService
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private const int MaximumCharacters = 16_000_000;
    public Task SaveAsync(string path, string script, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        var safePath = ValidatePath(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(safePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(safePath, script, Utf8, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public Task<string> LoadAsync(string path, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var stream = new FileStream(ValidatePath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
        var result = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (result.Length + read > MaximumCharacters) throw new InvalidDataException("Arquivo excede 16 milhões de caracteres. Abra um trecho menor no editor.");
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }, cancellationToken);

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path.Trim());
        if (!fullPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("O arquivo de script deve usar a extensão .js.", nameof(path));
        }

        return fullPath;
    }
}
