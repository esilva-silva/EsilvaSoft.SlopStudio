using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>Método efetivo de autenticação observado. Só <see cref="Subscription"/> libera o modo assinatura.</summary>
public enum ClaudeCodeAuthKind
{
    /// <summary>Conta claude.ai com assinatura, sem chave de API em uso.</summary>
    Subscription,

    NotLoggedIn,

    /// <summary><c>apiKeySource</c> presente (ex.: <c>ANTHROPIC_API_KEY</c>): cobrança pela API.</summary>
    ApiKey,

    /// <summary><c>apiKeyHelper</c> de configuração: cobrança pela API.</summary>
    ApiKeyHelper,

    /// <summary>Token no ambiente (<c>ANTHROPIC_AUTH_TOKEN</c> ou <c>CLAUDE_CODE_OAUTH_TOKEN</c>), indistinguíveis por status.</summary>
    EnvironmentToken,

    /// <summary>Provedor de nuvem (Bedrock, Vertex, Foundry) ou outro <c>apiProvider</c>.</summary>
    CloudProvider,

    /// <summary>Variável de ambiente que muda cobrança/destino ou indica outra sessão Claude Code hospedeira.</summary>
    BlockedEnvironment,

    /// <summary>Logado por método desconhecido ou conta sem assinatura.</summary>
    UnsupportedMethod,

    /// <summary>Saída ausente, grande demais, não JSON ou sem os campos obrigatórios.</summary>
    Unreadable,
}

/// <summary>
/// Resultado de <c>claude auth status</c> filtrado por allowlist de campos: nunca contém e-mail, organização, IDs de
/// conta, diretórios ou tokens. <see cref="EnvironmentVariableName"/> e <see cref="ApiKeySource"/> são somente nomes.
/// </summary>
public sealed record ClaudeCodeAuthStatus(
    ClaudeCodeAuthKind Kind,
    string? AuthMethod = null,
    string? ApiProvider = null,
    string? SubscriptionType = null,
    string? ApiKeySource = null,
    string? EnvironmentVariableName = null)
{
    public bool IsSubscription => Kind == ClaudeCodeAuthKind.Subscription;

    /// <summary>
    /// Variáveis que mudam a cobrança ou o destino (precedência documentada em Authentication) ou indicam que o app foi
    /// iniciado de dentro de outra sessão Claude Code (risco R-CL-02). O app não as remove nem injeta: bloqueia.
    /// </summary>
    public static IReadOnlyList<string> BlockingEnvironmentVariables { get; } =
    [
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_AUTH_TOKEN",
        "CLAUDE_CODE_OAUTH_TOKEN",
        "CLAUDE_CODE_USE_BEDROCK",
        "CLAUDE_CODE_USE_VERTEX",
        "CLAUDE_CODE_USE_FOUNDRY",
        "ANTHROPIC_BASE_URL",
        "CLAUDECODE",
        "CLAUDE_CODE_ENTRYPOINT",
    ];

    /// <summary>Primeira variável bloqueante definida (só o nome), ou nulo.</summary>
    internal static string? FindBlockingEnvironmentVariable(Func<string, bool>? isSet)
    {
        isSet ??= static name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
        return BlockingEnvironmentVariables.FirstOrDefault(isSet);
    }

    /// <summary>
    /// Classifica a saída JSON. Regra (spike P7-CL0-01): assinatura somente se <c>loggedIn</c> e
    /// <c>authMethod == "claude.ai"</c> e <c>apiKeySource</c> ausente e <c>subscriptionType</c> não nulo;
    /// <c>authMethod</c> sozinho não distingue (continua <c>claude.ai</c> com <c>ANTHROPIC_API_KEY</c>).
    /// </summary>
    public static ClaudeCodeAuthStatus Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || output.Length > 64 * 1024)
        {
            return new(ClaudeCodeAuthKind.Unreadable);
        }

        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("loggedIn", out var loggedIn) || loggedIn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return new(ClaudeCodeAuthKind.Unreadable);
            }

            // Allowlist: somente estes campos são lidos; qualquer outro (email, orgId, orgName, diretórios) é ignorado.
            var authMethod = SafeToken(root, "authMethod");
            var apiProvider = SafeToken(root, "apiProvider");
            var apiKeySource = root.TryGetProperty("apiKeySource", out var source) && source.ValueKind != JsonValueKind.Null
                ? SafeToken(root, "apiKeySource") ?? "unknown"
                : null;
            var subscriptionType = SafeToken(root, "subscriptionType");

            if (!loggedIn.GetBoolean())
            {
                return new(ClaudeCodeAuthKind.NotLoggedIn, authMethod, apiProvider);
            }

            var kind = Classify(authMethod, apiProvider, apiKeySource, subscriptionType);
            return new(kind, authMethod, apiProvider, subscriptionType, apiKeySource);
        }
        catch (JsonException)
        {
            return new(ClaudeCodeAuthKind.Unreadable);
        }
    }

    private static ClaudeCodeAuthKind Classify(string? authMethod, string? apiProvider, string? apiKeySource, string? subscriptionType)
    {
        if (apiProvider is not null && !string.Equals(apiProvider, "firstParty", StringComparison.Ordinal))
        {
            return ClaudeCodeAuthKind.CloudProvider;
        }

        if (string.Equals(authMethod, "api_key_helper", StringComparison.Ordinal) ||
            string.Equals(apiKeySource, "apiKeyHelper", StringComparison.Ordinal))
        {
            return ClaudeCodeAuthKind.ApiKeyHelper;
        }

        if (apiKeySource is not null)
        {
            return ClaudeCodeAuthKind.ApiKey;
        }

        if (string.Equals(authMethod, "oauth_token", StringComparison.Ordinal))
        {
            return ClaudeCodeAuthKind.EnvironmentToken;
        }

        return string.Equals(authMethod, "claude.ai", StringComparison.Ordinal) && subscriptionType is not null
            ? ClaudeCodeAuthKind.Subscription
            : ClaudeCodeAuthKind.UnsupportedMethod;
    }

    /// <summary>Valor curto só com <c>[A-Za-z0-9._-]</c>; qualquer outro conteúdo é descartado.</summary>
    private static string? SafeToken(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 and <= 64 } text &&
        text.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? text
            : null;
}
