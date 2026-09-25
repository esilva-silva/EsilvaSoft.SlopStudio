using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Composition options of the local agent broker. Everything is off by default: <see cref="Enabled"/> is the explicit
/// user opt-in and <see cref="Stage"/> the explicitly released catalog stage. Stages beyond
/// <see cref="AgentToolExposureStage.LiteralQueries"/> are refused until their gates are approved.
/// </summary>
/// <remarks>
/// Gate (fase 7, lotes 2/3): <see cref="AgentToolExposureStage.LiteralQueries"/> (<c>mongo_find</c>/<c>mongo_count</c>)
/// stays a configurable ceiling, not a release. The default remains <see cref="AgentToolExposureStage.None"/>; raising
/// it is an explicit composition decision that requires the lote 2 gate approved in
/// <c>docs/phases/phase-07-v0.11.0/17-validacao-da-meta.md</c>. Until then no product composition may set it.
/// </remarks>
public sealed record AgentBrokerOptions
{
    public const int MaximumConnectionsCeiling = 16;

    /// <summary>Identifies the IPC endpoint; not a credential.</summary>
    public Guid WorkspaceId { get; init; }

    /// <summary>Explicit user opt-in. Without it no broker, registry or endpoint is composed.</summary>
    public bool Enabled { get; init; }

    /// <summary>Most restrictive by default: nothing is discoverable or executable until a stage is set.</summary>
    public AgentToolExposureStage Stage { get; init; } = AgentToolExposureStage.None;

    public int MaximumConnections { get; init; } = 8;

    /// <summary>
    /// In-flight calls per proxy connection, capped by the registry session quota: the MCP session is the enrolled
    /// channel, so a higher value could never be admitted and would only turn into <c>Busy</c> after an audit intent.
    /// </summary>
    public int MaximumConcurrentCallsPerConnection { get; init; } = AgentToolRegistry.MaximumConcurrentCallsPerSession;

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
        if (MaximumConcurrentCallsPerConnection < 1 ||
            MaximumConcurrentCallsPerConnection > AgentToolRegistry.MaximumConcurrentCallsPerSession)
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
