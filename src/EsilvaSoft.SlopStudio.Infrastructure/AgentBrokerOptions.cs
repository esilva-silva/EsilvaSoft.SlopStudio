using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Composition options of the local agent broker. Everything is off by default: <see cref="Enabled"/> is the explicit
/// user opt-in and <see cref="Stage"/> the explicitly released catalog stage. Stages beyond
/// <see cref="AgentToolExposureStage.LiteralQueries"/> are refused until their gates are approved.
/// </summary>
public sealed record AgentBrokerOptions
{
    public const int MaximumConnectionsCeiling = 16;

    /// <summary>Identifies the IPC endpoint; not a credential.</summary>
    public Guid WorkspaceId { get; init; }

    /// <summary>Explicit user opt-in. Without it no broker, registry or endpoint is composed.</summary>
    public bool Enabled { get; init; }

    public AgentToolExposureStage Stage { get; init; } = AgentToolExposureStage.None;

    public int MaximumConnections { get; init; } = 8;
    public int MaximumConcurrentCallsPerConnection { get; init; } = 4;
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Registry execution ceiling (the registry itself caps at 30 s).</summary>
    public TimeSpan ToolExecutionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaximumAuthenticationFailuresPerChannel { get; init; } = 5;
    public int MaximumAuthenticationFailuresGlobal { get; init; } = 20;
    public TimeSpan AuthenticationFailureWindow { get; init; } = TimeSpan.FromMinutes(1);

    /// <exception cref="ArgumentException">Invalid option or unapproved stage.</exception>
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty) throw new ArgumentException("O broker exige um workspace.", nameof(WorkspaceId));
        if (!Enum.IsDefined(Stage)) throw new ArgumentException("Estágio de exposição desconhecido.", nameof(Stage));
        if (Stage > AgentToolExposureStage.LiteralQueries)
            throw new ArgumentException("Estágio de exposição ainda não liberado para MCP.", nameof(Stage));
        if (MaximumConnections is < 1 or > MaximumConnectionsCeiling)
            throw new ArgumentException("Limite de conexões inválido.", nameof(MaximumConnections));
        if (MaximumConcurrentCallsPerConnection is < 1 or > 8)
            throw new ArgumentException("Limite de chamadas simultâneas inválido.", nameof(MaximumConcurrentCallsPerConnection));
        if (HandshakeTimeout <= TimeSpan.Zero || HandshakeTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentException("Prazo de handshake inválido.", nameof(HandshakeTimeout));
        if (ToolExecutionTimeout <= TimeSpan.Zero || ToolExecutionTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentException("Prazo de execução inválido.", nameof(ToolExecutionTimeout));
        if (MaximumAuthenticationFailuresPerChannel < 1 || MaximumAuthenticationFailuresGlobal < MaximumAuthenticationFailuresPerChannel ||
            AuthenticationFailureWindow <= TimeSpan.Zero)
            throw new ArgumentException("Limites de autenticação inválidos.", nameof(MaximumAuthenticationFailuresPerChannel));
    }
}
