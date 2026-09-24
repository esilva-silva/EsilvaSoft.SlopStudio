using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public interface IAgentRuntime
{
    Task<AgentSessionId> StartSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken);

    IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentSessionId sessionId,
        AgentTurnRequest request,
        CancellationToken cancellationToken);

    Task CancelTurnAsync(AgentSessionId sessionId, AgentTurnId turnId, CancellationToken cancellationToken);

    Task CloseSessionAsync(AgentSessionId sessionId, CancellationToken cancellationToken);
}
