namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Códigos seguros emitidos em <c>AgentError</c> pelo modo Claude (assinatura). Fixos, ASCII, sem texto do CLI,
/// stderr, prompt, caminhos, e-mail, organização ou token.
/// </summary>
public static class ClaudeCodeErrorCodes
{
    public const string EmptyMessage = "EmptyMessage";
    public const string InputTooLarge = "InputTooLarge";
    public const string ExecutableUnavailable = "ClaudeCodeExecutableUnavailable";
    public const string NotLoggedIn = "ClaudeCodeNotLoggedIn";
    public const string NonSubscriptionAuthentication = "ClaudeCodeNonSubscriptionAuth";
    public const string BlockedEnvironment = "ClaudeCodeBlockedEnvironment";
    public const string AuthStatusUnavailable = "ClaudeCodeAuthStatusUnavailable";
    public const string StartFailed = "ClaudeCodeStartFailed";
    public const string InitMismatch = "ClaudeCodeInitMismatch";
    public const string ToolOutsideAllowlist = "ClaudeCodeToolOutsideAllowlist";
    public const string ProtocolViolation = "ClaudeCodeProtocolViolation";
    public const string OutputLimitExceeded = "ClaudeCodeOutputLimitExceeded";
    public const string StreamIncomplete = "ClaudeCodeStreamIncomplete";
    public const string ProcessFailed = "ClaudeCodeProcessFailed";
    public const string SessionNotFound = "ClaudeCodeSessionNotFound";
    public const string MaxTurnsReached = "ClaudeCodeMaxTurnsReached";
    public const string AuthenticationFailed = "ClaudeCodeAuthenticationFailed";
    public const string RateLimited = "ClaudeCodeRateLimited";
    public const string ProviderUnavailable = "ClaudeCodeProviderUnavailable";
    public const string RequestRejected = "ClaudeCodeRequestRejected";
    public const string ExecutionError = "ClaudeCodeExecutionError";
    public const string TurnTimeout = "TurnTimeout";
    public const string ProviderFailure = "ProviderFailure";

    /// <summary>Código de <c>ToolFailed</c> para ferramenta nativa de leitura que terminou com erro ou foi negada.</summary>
    public const string NativeToolFailed = "NativeToolFailed";

    /// <summary>Códigos que a sessão declara ao runtime (<c>IAgentSession.ProviderErrorCodes</c>) para chegarem à UI.</summary>
    public static IReadOnlyList<string> TurnErrorCodes { get; } =
    [
        EmptyMessage, InputTooLarge, ExecutableUnavailable, NotLoggedIn, NonSubscriptionAuthentication, BlockedEnvironment,
        AuthStatusUnavailable, StartFailed, InitMismatch, ToolOutsideAllowlist, ProtocolViolation, OutputLimitExceeded,
        StreamIncomplete, ProcessFailed, SessionNotFound, MaxTurnsReached, AuthenticationFailed, RateLimited,
        ProviderUnavailable, RequestRejected, ExecutionError, TurnTimeout, ProviderFailure,
    ];
}

/// <summary>Motivo seguro de indisponibilidade; o nome do membro é o <c>UnavailableCode</c> publicado.</summary>
public enum ClaudeCodeUnavailableReason
{
    None,
    InvalidConfiguration,
    ExecutableNotFound,
    UnsupportedExecutable,
    VersionTooLow,
    VersionUnreadable,
    ProbeTimedOut,
    ProbeFailed,
    NotLoggedIn,
    NonSubscriptionAuthentication,
    BlockedEnvironment,
    AuthStatusUnreadable,
    ModelNotAllowed,
    NoModelSelected,
}

/// <summary>Falha ao criar sessão; mensagem fixa em pt-BR, sem dados da conta, caminhos ou saída do CLI.</summary>
public sealed class ClaudeCodeUnavailableException(ClaudeCodeUnavailableReason reason)
    : InvalidOperationException(Describe(reason))
{
    public ClaudeCodeUnavailableReason Reason { get; } = reason;

    public static string Describe(ClaudeCodeUnavailableReason reason) => reason switch
    {
        ClaudeCodeUnavailableReason.ExecutableNotFound => "Claude (assinatura) indisponível: o Claude Code não foi encontrado. Instale-o pelo canal oficial.",
        ClaudeCodeUnavailableReason.UnsupportedExecutable => "Claude (assinatura) indisponível: só foi encontrado um atalho/script do Claude Code; é necessário o executável nativo.",
        ClaudeCodeUnavailableReason.VersionTooLow => "Claude (assinatura) indisponível: atualize o Claude Code.",
        ClaudeCodeUnavailableReason.VersionUnreadable or ClaudeCodeUnavailableReason.ProbeFailed =>
            "Claude (assinatura) indisponível: não foi possível verificar o Claude Code instalado.",
        ClaudeCodeUnavailableReason.ProbeTimedOut => "Claude (assinatura) indisponível: o Claude Code não respondeu a tempo.",
        ClaudeCodeUnavailableReason.NotLoggedIn => "Claude (assinatura) indisponível: entre pelo Claude Code.",
        ClaudeCodeUnavailableReason.NonSubscriptionAuthentication =>
            "Claude (assinatura) bloqueado: o Claude Code está usando outro método de autenticação (API Key, token ou nuvem), com cobrança diferente.",
        ClaudeCodeUnavailableReason.BlockedEnvironment =>
            "Claude (assinatura) bloqueado: há variável de ambiente que muda a autenticação ou o destino do Claude Code.",
        ClaudeCodeUnavailableReason.AuthStatusUnreadable => "Claude (assinatura) indisponível: o estado de login do Claude Code não pôde ser lido.",
        ClaudeCodeUnavailableReason.ModelNotAllowed => "Claude (assinatura) indisponível: o modelo escolhido não está permitido.",
        ClaudeCodeUnavailableReason.NoModelSelected => "Claude (assinatura) indisponível: nenhum modelo selecionado.",
        ClaudeCodeUnavailableReason.InvalidConfiguration => "Claude (assinatura) indisponível: configuração inválida.",
        _ => "Claude (assinatura) indisponível.",
    };
}
