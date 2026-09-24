namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>A fixed request; callers capture tab and document context before invoking the runtime.</summary>
public sealed record AgentTurnRequest(
    AgentTurnId TurnId,
    string UserMessage,
    string TabId,
    long DocumentVersion,
    string? AuthorizedContext = null);
