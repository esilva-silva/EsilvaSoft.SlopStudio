using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public interface IAgentProvider
{
    string ProviderId { get; }

    Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken);
}

public interface IAgentSession : IAsyncDisposable
{
    IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken);

    Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken);
}
