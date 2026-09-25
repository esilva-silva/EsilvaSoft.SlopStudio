using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Composition options of the agent platform shared by every ingress (native chat runtime and the opt-in MCP broker).
/// The registry is always composed, but closed by default: <see cref="ToolExposureStage"/> starts at
/// <see cref="AgentToolExposureStage.None"/>, so nothing is discoverable or executable until a stage is released
/// explicitly. Stages beyond <see cref="AgentToolExposureStage.LiteralQueries"/> are refused until their gates are
/// approved. Releasing a stage still grants nothing: every call needs a persisted grant for its principal.
/// </summary>
public sealed record AgentPlatformOptions
{
    /// <summary>Catalog stage released to the single shared registry, for every ingress alike.</summary>
    public AgentToolExposureStage ToolExposureStage { get; init; } = AgentToolExposureStage.None;

    /// <summary>Registry execution ceiling (the registry itself caps at 30 s).</summary>
    public TimeSpan ToolExecutionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Runtime limits; validated by the runtime itself when it is created.</summary>
    public AgentRuntimeOptions Runtime { get; init; } = AgentRuntimeOptions.Default;

    /// <exception cref="ArgumentException">Invalid option or unapproved stage.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(ToolExposureStage))
            throw new ArgumentException("Estágio de exposição desconhecido.", nameof(ToolExposureStage));
        if (ToolExposureStage > AgentToolExposureStage.LiteralQueries)
            throw new ArgumentException("Estágio de exposição ainda não liberado.", nameof(ToolExposureStage));
        if (ToolExecutionTimeout <= TimeSpan.Zero || ToolExecutionTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentException("Prazo de execução inválido.", nameof(ToolExecutionTimeout));
        if (Runtime is null) throw new ArgumentException("Opções do runtime ausentes.", nameof(Runtime));
    }
}
