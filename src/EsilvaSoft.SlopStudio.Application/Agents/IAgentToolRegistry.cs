using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Shared metadata and invocation boundary for every agent ingress. Implementations own schema validation and
/// authorization; adapters must not execute handlers directly.
/// </summary>
public interface IAgentToolRegistry
{
    IReadOnlyList<AgentToolDescriptor> GetDescriptors();
    AgentToolDescriptor? FindDescriptor(string? name);
    string? GetInputSchemaJson(string? name);
    string? GetOutputSchemaJson(string? name);
    Task<AgentToolInvocationResult> InvokeAsync(
        AgentPrincipal? principal,
        AgentInvocationContext? invocationContext,
        AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope,
        string? name,
        string? argumentsJson,
        CancellationToken cancellationToken = default);
}
