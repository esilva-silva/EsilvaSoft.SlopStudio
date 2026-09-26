using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.ClaudeCode;

/// <summary>
/// Ambiente isolado por teste para o CLI falso (<c>tests/EsilvaSoft.SlopStudio.FakeClaudeCode</c>, executável nativo
/// chamado <c>claude</c>): pasta dedicada (cwd, com o cenário e o log do falso), diretório de dados e LiteDB falsos.
/// Nenhum teste automatizado usa o Claude Code real, conta, rede ou <c>~/.claude</c>.
/// </summary>
internal sealed class ClaudeCodeFixture : IDisposable
{
    /// <summary>Canários: campos de conta do auth status e texto de stderr que nunca podem chegar a eventos/erros.</summary>
    public const string EmailCanary = "canario-conta@example.invalid";
    public const string OrgCanary = "org-canario-7f3a";
    public const string StderrCanary = "sk-ant-canario-stderr";

    private readonly JsonObject _scenario = new();

    public ClaudeCodeFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "slop-claude-code-tests", Guid.NewGuid().ToString("N"));
        WorkingDirectory = Path.Combine(Root, "cwd");
        AppData = Path.Combine(Root, "appdata", "SlopStudio");
        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(AppData);
        AuthStatus(SubscriptionStatus);
    }

    public static string SubscriptionStatus =>
        $$"""{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","email":"{{EmailCanary}}","orgId":"{{OrgCanary}}","orgName":"Org Canario","subscriptionType":"pro","analyticsDisabled":false,"projectsDirectory":"<HOME>/.claude/projects","configDirectory":"<HOME>/.claude"}""";

    public string Root { get; }

    public string WorkingDirectory { get; }

    public string AppData { get; }

    public static string FakeExecutable
    {
        get
        {
            var testsBin = AppContext.BaseDirectory;
            var candidate = testsBin.Replace("EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests", "EsilvaSoft.SlopStudio.FakeClaudeCode",
                StringComparison.Ordinal);
            var path = Path.Combine(candidate, OperatingSystem.IsWindows() ? "claude.exe" : "claude");
            if (!File.Exists(path))
            {
                Assert.Fail("CLI falso não compilado: execute dotnet build EsilvaSoft.SlopStudio.slnx. Esperado em " + path);
            }

            return path;
        }
    }

    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "ClaudeCode", "Fixtures", name);

    public ClaudeCodeFixture AuthStatus(string json, int exitCode = 0)
    {
        _scenario["authStatus"] = JsonNode.Parse(json);
        _scenario["authExitCode"] = exitCode;
        return Save();
    }

    public ClaudeCodeFixture Version(string version, int delayMs = 0)
    {
        _scenario["version"] = version;
        _scenario["versionDelayMs"] = delayMs;
        return Save();
    }

    public ClaudeCodeFixture Turn(string fixture, string? resumeFixture = null)
    {
        _scenario["turnFixture"] = FixturePath(fixture);
        if (resumeFixture is not null)
        {
            _scenario["resumeFixture"] = FixturePath(resumeFixture);
        }

        return Save();
    }

    public ClaudeCodeFixture AuthDelay(int delayMs)
    {
        _scenario["authDelayMs"] = delayMs;
        return Save();
    }

    public ClaudeCodeFixture ResumeMissing(bool missing = true)
    {
        _scenario["resumeMissing"] = missing;
        return Save();
    }

    public ClaudeCodeFixture Save(string? directory = null)
    {
        File.WriteAllText(Path.Combine(directory ?? WorkingDirectory, "fake-claude.json"), _scenario.ToJsonString());
        return this;
    }

    public ClaudeCodeAgentProviderOptions Options(
        Func<string, bool>? environment = null, TimeSpan? turnDuration = null, Func<string?>? workspace = null,
        string? executable = null) => new()
    {
        ExecutablePath = executable ?? FakeExecutable,
        // As fixtures do spike foram gravadas com haiku; o init é validado contra o modelo pedido (M4).
        DefaultModel = "haiku",
        DedicatedWorkingDirectory = WorkingDirectory,
        AppDataDirectory = AppData,
        DatabasePath = Path.Combine(AppData, "workspace.db"),
        WorkspaceDirectory = workspace,
        ProbeTimeout = TimeSpan.FromSeconds(15),
        MaxTurnDuration = turnDuration ?? TimeSpan.FromSeconds(60),
        IsEnvironmentVariableSet = environment ?? (static _ => false),
    };

    public ClaudeCodeAgentProvider Provider(ClaudeCodeAgentProviderOptions? options = null) => new(options ?? Options());

    /// <summary>Registros do CLI falso (argv, stdin, filhos) no cwd indicado.</summary>
    public IReadOnlyList<JsonElement> Log(string? directory = null)
    {
        var path = Path.Combine(directory ?? WorkingDirectory, "fake-claude.log.jsonl");
        if (!File.Exists(path))
        {
            return [];
        }

        string[] lines;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        return [.. lines.Select(static line => JsonDocument.Parse(line).RootElement.Clone())];
    }

    public IReadOnlyList<string[]> Invocations(string? directory = null) =>
        [.. Log(directory).Where(static e => e.GetProperty("event").GetString() == "start")
            .Select(static e => e.GetProperty("argv").EnumerateArray().Select(static a => a.GetString()!).ToArray())];

    public IReadOnlyList<string[]> TurnInvocations(string? directory = null) =>
        [.. Invocations(directory).Where(static argv => argv.Contains("-p"))];

    public static async Task<List<AgentProviderEvent>> RunAsync(IAgentSession session, string message = "Responda apenas: ok",
        CancellationToken cancellationToken = default)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(new AgentTurnRequest(AgentTurnId.New(), message, "tab-1", 1), cancellationToken))
        {
            events.Add(item);
        }

        return events;
    }

    public static string Text(IEnumerable<AgentProviderEvent> events) =>
        string.Concat(events.Where(static e => e.Kind == AgentEventKind.MessageDelta).Select(static e => e.Text));

    public static string? Error(IEnumerable<AgentProviderEvent> events) =>
        events.SingleOrDefault(static e => e.Kind == AgentEventKind.AgentError)?.Text;

    public void Dispose()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
