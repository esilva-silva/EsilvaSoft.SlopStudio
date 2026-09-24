namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>Adapter output. Native provider identifiers and exceptions stay inside the adapter.</summary>
public sealed record AgentProviderEvent(AgentEventKind Kind, string? Text = null);
