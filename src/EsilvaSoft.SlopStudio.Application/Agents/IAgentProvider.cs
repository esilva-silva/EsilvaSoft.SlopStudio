using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public interface IAgentProvider
{
    string ProviderId { get; }

    /// <summary>
    /// True only for an in-process provider that sends nothing off the machine (ONNX facade, lote 9). The runtime
    /// derives the tool output destination from this declaration, never from a binding or provider output, and the
    /// presentation destination (Local/External) comes from the same member.
    /// </summary>
    bool IsLocal => false;

    /// <summary>
    /// Static, side-effect free description: no vault access, network call or authentication. Capabilities are only
    /// those the adapter proved (with their evidence). The default declares nothing beyond the ID.
    /// </summary>
    AgentProviderDescriptor Describe() => AgentProviderDescriptor.Minimal(ProviderId);

    /// <summary>
    /// Dynamic availability without side effects: it may read local configuration and whether a vault item exists,
    /// but never calls the provider service, sends data or starts authentication. The default fails closed.
    /// </summary>
    Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(AgentProviderStatus.NotReported);

    Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken);
}

public interface IAgentSession : IAsyncDisposable
{
    IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken);

    Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken);

    Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken);

    Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken);
}
