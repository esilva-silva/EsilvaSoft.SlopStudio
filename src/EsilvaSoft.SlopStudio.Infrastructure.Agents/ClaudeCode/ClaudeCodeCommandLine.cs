using System.Globalization;
using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>Onde o turno roda: pasta de workspace do usuário ou pasta dedicada vazia do app.</summary>
public enum ClaudeCodeWorkingDirectoryKind
{
    /// <summary>Pasta dedicada vazia; toda leitura é <c>ask</c> e, sem ferramenta de aprovação, é negada.</summary>
    Dedicated,

    /// <summary>Pasta de workspace do usuário; Read/Glob/Grep dentro dela não pedem aprovação (ADR-054 revisada).</summary>
    Workspace,
}

/// <summary>Configuração de execução fixada na criação da sessão (cwd, regras e argv globais).</summary>
internal sealed record ClaudeCodeLaunchProfile(
    string ExecutablePath,
    ClaudeCodeVersion Version,
    string WorkingDirectory,
    ClaudeCodeWorkingDirectoryKind WorkingDirectoryKind,
    string SettingsJson,
    string Model)
{
    /// <summary>
    /// Flags globais compartilhadas pelo <c>auth status</c> preventivo e pelo turno: são as que alteram a
    /// autenticação observada (spike: <c>auth status</c> reflete <c>--settings</c>/<c>--setting-sources</c>).
    /// </summary>
    public IReadOnlyList<string> GlobalArguments => ["--setting-sources", "user", "--settings", SettingsJson];
}

/// <summary>
/// Argv do Claude Code montado só com valores validados (nunca texto do usuário/modelo) e passado por
/// <c>ArgumentList</c>, sem shell. Sem <c>--bare</c> (nunca usa a assinatura) e sem <c>--console</c>.
/// </summary>
internal static class ClaudeCodeCommandLine
{
    public static IReadOnlyList<string> VersionArguments { get; } = ["--version"];

    public static IReadOnlyList<string> LoginArguments { get; } = ["auth", "login"];

    public static IReadOnlyList<string> LogoutArguments { get; } = ["auth", "logout"];

    public static IReadOnlyList<string> AuthStatusArguments(ClaudeCodeLaunchProfile? profile) =>
        profile is null ? ["auth", "status"] : [.. profile.GlobalArguments, "auth", "status"];

    /// <summary>
    /// Turno: um processo por mensagem, stream-json nos dois sentidos, allowlist exata de ferramentas nativas,
    /// nenhum MCP (<c>--strict-mcp-config</c> sem <c>--mcp-config</c>), hooks desligados e fontes só do usuário.
    /// </summary>
    public static IReadOnlyList<string> TurnArguments(ClaudeCodeLaunchProfile profile, int maxTurns, string sessionId, bool resume)
    {
        if (!Guid.TryParseExact(sessionId, "D", out _))
        {
            throw new ArgumentException("Sessão do Claude Code inválida.", nameof(sessionId));
        }

        return
        [
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--include-partial-messages",
            "--verbose",
            "--permission-mode", "default",
            "--strict-mcp-config",
            "--tools", string.Join(',', ClaudeCodeAgentProviderOptions.NativeToolAllowlist),
            "--max-turns", maxTurns.ToString(CultureInfo.InvariantCulture),
            "--model", profile.Model,
            resume ? "--resume" : "--session-id", sessionId,
            .. profile.GlobalArguments,
        ];
    }

    /// <summary>Alias ou ID de modelo: letras, dígitos, <c>.</c>, <c>-</c>, <c>_</c>, <c>[</c>, <c>]</c>; nunca começa com <c>-</c>.</summary>
    public static bool IsSafeModelId(string? model) =>
        model is { Length: > 0 and <= 128 } && char.IsAsciiLetterOrDigit(model[0]) &&
        model.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '[' or ']');

    /// <summary>
    /// <c>--settings</c> do Slop: hooks desligados, <c>deny</c> determinístico para o diretório de dados do app, o
    /// LiteDB, <c>~/.claude</c> e <c>~/.ssh</c>, e <c>ask</c> para toda leitura quando não há pasta de workspace. As
    /// regras de <c>Read</c> também valem para Glob/Grep (documentação; confirmado para Glob no spike).
    /// </summary>
    public static string BuildSettingsJson(ClaudeCodeWorkingDirectoryKind kind, IReadOnlyList<string> denyRules)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("disableAllHooks", true);
            writer.WriteStartObject("permissions");
            writer.WriteStartArray("deny");
            foreach (var rule in denyRules)
            {
                writer.WriteStringValue(rule);
            }

            writer.WriteEndArray();
            if (kind == ClaudeCodeWorkingDirectoryKind.Dedicated)
            {
                writer.WriteStartArray("ask");
                foreach (var tool in ClaudeCodeAgentProviderOptions.NativeToolAllowlist)
                {
                    writer.WriteStringValue(tool);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Regras <c>Read(...)</c> para caminhos protegidos. Dentro do perfil usa <c>~/</c> (independe de caracteres do nome
    /// de usuário); fora dele usa a forma absoluta <c>//</c> (no Windows, <c>C:\x</c> vira <c>//c/x</c>, normalização
    /// POSIX documentada). Caminho com caractere especial de padrão é recusado em vez de gerar regra ambígua.
    /// </summary>
    public static IReadOnlyList<string> BuildDenyRules(string appDataDirectory, string databasePath, string homeDirectory)
    {
        List<string> rules =
        [
            ReadRule(appDataDirectory, homeDirectory, directory: true),
            ReadRule(databasePath, homeDirectory, directory: false),
            "Read(~/.claude/**)",
            "Read(~/.ssh/**)",
        ];
        return [.. rules.Distinct(StringComparer.Ordinal)];
    }

    private static string ReadRule(string path, string home, bool directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var homeFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        string pattern;
        if (ClaudeCodeWorkspacePolicy.IsSameOrInside(full, homeFull) && !ClaudeCodeWorkspacePolicy.PathEquals(full, homeFull))
        {
            pattern = "~/" + Path.GetRelativePath(homeFull, full).Replace('\\', '/');
        }
        else if (OperatingSystem.IsWindows() && full.Length >= 2 && full[1] == ':')
        {
            pattern = "//" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
        }
        else
        {
            pattern = "/" + full.Replace('\\', '/');
        }

        if (pattern.Any(static c => c is '(' or ')' or '*' or '?' or '[' or ']' or '{' or '}' or '!' or '"' || char.IsControl(c)))
        {
            throw new InvalidOperationException("Caminho protegido com caractere não suportado em regra de permissão.");
        }

        return "Read(" + pattern + (directory ? "/**)" : ")");
    }
}
