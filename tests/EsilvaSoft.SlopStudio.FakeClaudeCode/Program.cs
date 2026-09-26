using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.SlopStudio.FakeClaudeCode;

/// <summary>
/// Claude Code falso. O cenário vem de <c>fake-claude.json</c> no diretório de trabalho (o mesmo cwd que o adapter
/// usa), e cada invocação registra argv e stdin em <c>fake-claude.log.jsonl</c> para as asserções dos testes.
/// Diretivas de fixture (linhas iniciadas por <c>#</c>): <c>#spawn-child</c>, <c>#hang</c>, <c>#garbage</c>,
/// <c>#giant N</c>, <c>#stderr texto</c>, <c>#fragment</c>, <c>#sleep ms</c>, <c>#exit N</c>, <c>#spawn-grandchild</c>
/// (filho intermediário cria um neto e sai, deixando o neto órfão).
/// </summary>
internal static class Program
{
    private const string ScenarioFile = "fake-claude.json";
    private const string LogFile = "fake-claude.log.jsonl";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Lock LogGate = new();

    private static int Main(string[] args)
    {
        if (args is ["--fake-sleep-child"])
        {
            Thread.Sleep(TimeSpan.FromMinutes(3));
            return 0;
        }

        if (args is ["--fake-grandparent"])
        {
            // Cria o neto e sai: o neto fica órfão (pai morto), fora do alcance de Kill(entireProcessTree).
            var grandchild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fake-sleep-child") { UseShellExecute = false })!;
            Log(new Dictionary<string, object?> { ["event"] = "grandchild", ["pid"] = grandchild.Id });
            return 0;
        }

        using var scenarioDocument = LoadScenario();
        var scenario = scenarioDocument.RootElement;
        Log(new Dictionary<string, object?> { ["event"] = "start", ["pid"] = Environment.ProcessId, ["argv"] = args });

        if (args.Contains("--version"))
        {
            Sleep(scenario, "versionDelayMs");
            WriteStdout(Text(scenario, "version") ?? "2.1.268 (Claude Code)");
            return Int(scenario, "versionExitCode") ?? 0;
        }

        var authIndex = Array.IndexOf(args, "auth");
        if (authIndex >= 0 && authIndex + 1 < args.Length)
        {
            var subcommand = args[authIndex + 1];
            if (subcommand == "status")
            {
                Sleep(scenario, "authDelayMs");
                WriteStdout(scenario.TryGetProperty("authStatus", out var status) ? status.GetRawText() : "{\"loggedIn\":false}");
                return Int(scenario, "authExitCode") ?? 0;
            }

            // login/logout: só registra; o fluxo real termina no navegador da Anthropic.
            return 0;
        }

        if (args.Contains("-p"))
        {
            return RunTurn(scenario, args);
        }

        Console.Error.WriteLine("fake claude: argumentos não suportados");
        return 2;
    }

    private static int RunTurn(JsonElement scenario, string[] args)
    {
        var resume = args.Contains("--resume");
        var sessionId = ValueAfter(args, resume ? "--resume" : "--session-id") ?? "missing";
        // Como o CLI real: nada é emitido antes da primeira mensagem no stdin.
        var first = Console.In.ReadLine();
        Log(new Dictionary<string, object?> { ["event"] = "stdin", ["line"] = first });
        if (first is null)
        {
            return 0;
        }

        if (resume && scenario.TryGetProperty("resumeMissing", out var missing) && missing.ValueKind == JsonValueKind.True)
        {
            Console.Error.WriteLine("No conversation found with session ID: " + sessionId);
            WriteStdout("{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"num_turns\":0,\"session_id\":\"" + sessionId +
                "\",\"total_cost_usd\":0,\"permission_denials\":[],\"errors\":[\"No conversation found with session ID: " + sessionId + "\"]}");
            DrainStdin();
            return 1;
        }

        var fixture = Text(scenario, resume && scenario.TryGetProperty("resumeFixture", out _) ? "resumeFixture" : "turnFixture");
        var lines = fixture is null ? [] : File.ReadAllLines(fixture, Utf8);
        var fragment = false;
        foreach (var raw in lines)
        {
            var line = raw.Replace("{SESSION_ID}", sessionId, StringComparison.Ordinal);
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                var parts = line.Split(' ', 2);
                switch (parts[0])
                {
                    case "#spawn-child":
                        var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fake-sleep-child") { UseShellExecute = false })!;
                        Log(new Dictionary<string, object?> { ["event"] = "child", ["pid"] = child.Id });
                        WriteStdout("{\"type\":\"system\",\"subtype\":\"fake_child\",\"pid\":" + child.Id.ToString(CultureInfo.InvariantCulture) + "}");
                        break;
                    case "#spawn-grandchild":
                        using (var middle = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fake-grandparent") { UseShellExecute = false })!)
                        {
                            middle.WaitForExit();
                        }

                        break;
                    case "#hang":
                        Thread.Sleep(Timeout.Infinite);
                        break;
                    case "#garbage":
                        WriteStdout("isto nao e json {");
                        break;
                    case "#giant":
                        var size = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        WriteStdout("{\"type\":\"assistant\",\"blob\":\"" + new string('x', size) + "\"}");
                        break;
                    case "#stderr":
                        Console.Error.WriteLine(parts.Length > 1 ? parts[1] : string.Empty);
                        break;
                    case "#fragment":
                        fragment = true;
                        break;
                    case "#sleep":
                        Thread.Sleep(int.Parse(parts[1], CultureInfo.InvariantCulture));
                        break;
                    case "#exit":
                        Console.Out.Flush();
                        return int.Parse(parts[1], CultureInfo.InvariantCulture);
                }

                continue;
            }

            if (fragment)
            {
                WriteFragmented(line);
            }
            else
            {
                WriteStdout(line);
            }
        }

        // Como o CLI real em stream-json: espera mais entrada até o stdin fechar.
        DrainStdin();
        return Int(scenario, "exitCode") ?? 0;
    }

    private static void DrainStdin()
    {
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            Log(new Dictionary<string, object?> { ["event"] = "stdin", ["line"] = line });
        }

        Log(new Dictionary<string, object?> { ["event"] = "stdin-closed" });
    }

    private static void WriteFragmented(string line)
    {
        var bytes = Utf8.GetBytes(line + "\n");
        using var stdout = Console.OpenStandardOutput();
        for (var offset = 0; offset < bytes.Length; offset += 7)
        {
            stdout.Write(bytes, offset, Math.Min(7, bytes.Length - offset));
            stdout.Flush();
            Thread.Sleep(1);
        }
    }

    private static void WriteStdout(string line)
    {
        var bytes = Utf8.GetBytes(line + "\n");
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
    }

    private static JsonDocument LoadScenario()
    {
        var path = Path.Combine(Environment.CurrentDirectory, ScenarioFile);
        return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path, Utf8)) : JsonDocument.Parse("{}");
    }

    private static void Log(Dictionary<string, object?> entry)
    {
        // Sem cenário no cwd (ex.: consultas de estado na pasta temporária), nada é gravado: nenhum teste depende disso
        // e a pasta temporária do usuário não recebe arquivos do falso.
        if (!File.Exists(Path.Combine(Environment.CurrentDirectory, ScenarioFile)))
        {
            return;
        }

        var line = JsonSerializer.Serialize(entry) + "\n";
        lock (LogGate)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Environment.CurrentDirectory, LogFile), line, Utf8);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
            }
        }
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string? Text(JsonElement scenario, string name) =>
        scenario.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement scenario, string name) =>
        scenario.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static void Sleep(JsonElement scenario, string name)
    {
        if (Int(scenario, name) is { } delay)
        {
            Thread.Sleep(delay);
        }
    }
}
