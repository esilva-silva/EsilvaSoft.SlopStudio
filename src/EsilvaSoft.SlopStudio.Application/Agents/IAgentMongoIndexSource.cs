using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Bounded index metadata projection for the internal agent registry.</summary>
public interface IAgentMongoIndexSource
{
    Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
        string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken);
}

public sealed record AgentMongoIndexSummary(string Name, IReadOnlyList<string> KeyFields,
    bool Unique, bool Sparse, bool Hidden);

public sealed record AgentMongoIndexPage(IReadOnlyList<AgentMongoIndexSummary> Indexes,
    bool Truncated, bool TargetVerified);
