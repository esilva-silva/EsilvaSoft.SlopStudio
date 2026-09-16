using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class MongoshScriptExecutionService(IEnvironmentVaultRepository? environments = null, IConnectionSecretStore? secrets = null) : IScriptExecutionService
{
    public async Task<ScriptExecutionResult> ExecuteAsync(
        ConnectionProfile profile,
        string script,
        string? inputJson = null,
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        profile.EnsureWriteAllowed();
        var environment = new OperationEnvironment(environments, secrets, profile.Id);
        await environment.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var connectionString = environment.ResolvedConnection;
        var safeInput = MongoshScriptTemplate.ValidateInput(inputJson);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"slopdataadmin-{Guid.NewGuid():N}.js");
        var source = MongoshScriptTemplate.BuildScript(safeInput, script, database ?? profile.DefaultDatabase);
        var startInfo = BuildStartInfo(connectionString, scriptPath, environment.ScriptValues);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await File.WriteAllTextAsync(scriptPath, source, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellationRegistration = cancellationToken.Register(() => TryTerminate(process));
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            var parsed = MongoshOutputParser.Parse(standardOutput, MongoshScriptTemplate.ResultPrefix);

            return new ScriptExecutionResult(
                process.ExitCode,
                parsed.Results,
                parsed.ConsoleOutput,
                standardError,
                Stopwatch.GetElapsedTime(startedAt));
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Não foi possível localizar o executável mongosh. Configure SLOPDATAADMIN_MONGOSH_PATH ou instale o MongoDB Shell.", exception);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    public static ProcessStartInfo BuildStartInfo(string connectionString, string scriptPath, IReadOnlyDictionary<string, string>? values = null)
    {
        var executable = Environment.GetEnvironmentVariable("SLOPDATAADMIN_MONGOSH_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(executable)
                ? OperatingSystem.IsWindows() ? "mongosh.exe" : "mongosh"
                : executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--norc");
        startInfo.ArgumentList.Add("--nodb");
        startInfo.Environment["SLOP_CONNECTION_URI"] = connectionString;
        startInfo.Environment["SLOP_ENVIRONMENT_VALUES"] = JsonSerializer.Serialize(values ?? new Dictionary<string, string>());
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(scriptPath);
        return startInfo;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // O processo já terminou antes do cancelamento.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // O arquivo temporário é inofensivo e será removido pelo sistema quando possível.
        }
    }
}
