using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class MongoshScriptExecutionService(IEnvironmentVaultRepository? environments = null, IConnectionSecretStore? secrets = null) : IScriptExecutionService
{
    private const string ResultPrefix = "__SLOPDATAADMIN_RESULT__";

    internal const string DateHelpers = """
        const Date = new Proxy(globalThis.Date, {
          apply: (target, receiver, args) => args.length === 0 ? target() : new target(...__slopDateArguments(args)),
          construct: (target, args) => new target(...__slopDateArguments(args))
        });
        function __slopDateArguments(args) {
          if (args.length !== 1 || typeof args[0] !== "string" || !/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}$/.test(args[0])) return args;
          const iso = args[0].replace(" ", "T") + "Z";
          const date = new globalThis.Date(iso);
          if (!Number.isFinite(date.getTime()) || date.toISOString() !== iso) throw new Error("Data inválida: " + args[0]);
          return [date.getTime()];
        }
        """;

    /// <summary>
    /// mongosh cannot call the IDE codec, so CGUUID/JUUID/GUUID are defined over its native UUID and BinData.
    /// Unit tests execute this text and compare every byte with <see cref="UuidCodec"/>.
    /// </summary>
    internal const string UuidHelpers = """
        const __slopUuidHex = (name, value) => {
          if (typeof value !== "string" || !/^(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$/.test(value))
            throw new Error(name + "(...) exige um UUID com 32 dígitos hexadecimais, com ou sem hífens.");
          return value.replace(/-/g, "").toLowerCase();
        };
        const __slopLegacyUuid = (name, value, groups) => {
          const bytes = __slopUuidHex(name, value).match(/../g).map((pair) => parseInt(pair, 16));
          let offset = 0;
          for (const size of groups) { bytes.splice(offset, size, ...bytes.slice(offset, offset + size).reverse()); offset += size; }
          const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
          let base64 = "";
          for (let i = 0; i < 16; i += 3) {
            const chunk = (bytes[i] << 16) | ((bytes[i + 1] ?? 0) << 8) | (bytes[i + 2] ?? 0);
            base64 += alphabet[(chunk >> 18) & 63] + alphabet[(chunk >> 12) & 63] + (i + 1 < 16 ? alphabet[(chunk >> 6) & 63] : "=") + (i + 2 < 16 ? alphabet[chunk & 63] : "=");
          }
          return BinData(3, base64);
        };
        function CGUUID(value) { return __slopLegacyUuid("CGUUID", value, [4, 2, 2]); }
        function JUUID(value) { return __slopLegacyUuid("JUUID", value, [8, 8]); }
        function GUUID(value) { return UUID(__slopUuidHex("GUUID", value)); }
        """;

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
        var safeInput = ValidateInput(inputJson);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"slopdataadmin-{Guid.NewGuid():N}.js");
        var source = BuildScript(safeInput, script, database ?? profile.DefaultDatabase);
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
            var parsed = MongoshOutputParser.Parse(standardOutput, ResultPrefix);

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

    public static string BuildScript(string inputJson, string userScript, string? database = null)
    {
        var literal = JsonSerializer.Serialize(inputJson);
        var databaseSelection = string.IsNullOrWhiteSpace(database) ? "" : $"db = db.getSiblingDB({JsonSerializer.Serialize(database)});";
        return $$"""
            const __slopUri = process.env.SLOP_CONNECTION_URI;
            delete process.env.SLOP_CONNECTION_URI;
            const __slopEnvironment = JSON.parse(process.env.SLOP_ENVIRONMENT_VALUES || "{}");
            delete process.env.SLOP_ENVIRONMENT_VALUES;
            const ENV = Object.freeze({ get: (key) => {
              if (!Object.prototype.hasOwnProperty.call(__slopEnvironment, key)) throw new Error("Chave de ambiente não definida: " + key);
              return __slopEnvironment[key];
            } });
            {{UuidHelpers}}
            {{DateHelpers}}
            if (__slopUri) db = connect(__slopUri);
            {{databaseSelection}}
            const __slopInputJson = {{literal}};
            const slop = Object.freeze({
              input: Object.freeze({ query: EJSON.parse(__slopInputJson), parameters: EJSON.parse(__slopInputJson) }),
              results: Object.freeze({
                emit: (document) => print("{{ResultPrefix}}" + EJSON.stringify(document, null, 0)),
                stream: async (cursor, maximum = 1000) => {
                  let emitted = 0;
                  while (await cursor.hasNext() && emitted < maximum) {
                    print("{{ResultPrefix}}" + EJSON.stringify(await cursor.next(), null, 0));
                    emitted += 1;
                  }
                  return emitted;
                }
              })
            });

            // --- início do script do usuário ---
            {{userScript}}
            // --- fim do script do usuário ---
            """;
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

    private static string ValidateInput(string? inputJson)
    {
        var input = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson;

        try
        {
            input = IdentifierRepresentationService.RewriteConstructors(input);
            _ = BsonDocument.Parse(input);
            return input;
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"A entrada do script não contém Extended JSON válido: {exception.Message}", nameof(inputJson), exception);
        }
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

public static class MongoshOutputParser
{
    public static ParsedMongoshOutput Parse(string standardOutput, string resultPrefix)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPrefix);
        var results = new List<string>();
        var console = new StringBuilder();
        using var reader = new StringReader(standardOutput);

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith(resultPrefix, StringComparison.Ordinal))
            {
                results.Add(line[resultPrefix.Length..]);
            }
            else
            {
                console.Append(line).Append('\n');
            }
        }

        return new ParsedMongoshOutput(results, console.ToString().TrimEnd());
    }
}

public sealed record ParsedMongoshOutput(IReadOnlyList<string> Results, string ConsoleOutput);
