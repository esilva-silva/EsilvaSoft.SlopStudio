using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Unitary, literal Mongo writes used only after agent authorization, an operation-bound approval and a durable
/// audit intent. Implementations never upsert, never touch more than one document, never retry after the command
/// may have reached the server, and never resolve ENV/JavaScript in inputs.
/// </summary>
public interface IAgentMongoWriteSource
{
    /// <summary>Reads the current target so the approval can show and bind the exact before-state.</summary>
    Task<AgentMongoWriteSnapshot> ReadTargetAsync(ConnectionProfile profile, AgentMongoWriteTarget target,
        CancellationToken cancellationToken);

    Task<AgentMongoWriteResult> InsertOneAsync(ConnectionProfile profile, AgentMongoInsertRequest request,
        CancellationToken cancellationToken);

    /// <summary>Update-operator document applied to exactly one <c>_id</c>, atomically conditioned on the approved hash.</summary>
    Task<AgentMongoWriteResult> UpdateOneAsync(ConnectionProfile profile, AgentMongoUpdateRequest request,
        CancellationToken cancellationToken);

    Task<AgentMongoWriteResult> DeleteOneAsync(ConnectionProfile profile, AgentMongoDeleteRequest request,
        CancellationToken cancellationToken);

    Task<AgentMongoWriteResult> CreateIndexAsync(ConnectionProfile profile, AgentMongoCreateIndexRequest request,
        CancellationToken cancellationToken);

    /// <summary>Never drops <c>_id_</c>; atomically rechecks the approved definition hash when the server allows it.</summary>
    Task<AgentMongoWriteResult> DropIndexAsync(ConnectionProfile profile, AgentMongoDropIndexRequest request,
        CancellationToken cancellationToken);
}

public enum AgentMongoWriteTargetKind
{
    Document = 1,
    Index = 2,
}

/// <summary>Document target uses <see cref="IdEjson"/>; index target uses <see cref="IndexName"/>.</summary>
public sealed record AgentMongoWriteTarget(AgentMongoWriteTargetKind Kind, string Database, string Collection,
    string? IdEjson, string? IndexName, int MaxTimeMs);

/// <summary>
/// Before-state for approval. <see cref="StateHash"/> is lowercase hex SHA-256 of the canonical Extended JSON of the
/// document (or of the index definition), computed by the source; <see cref="Exists"/> false means no target.
/// </summary>
public sealed record AgentMongoWriteSnapshot(bool Exists, string? StateEjson, string? StateHash,
    bool TargetVerified, bool ResultTooLarge);

public sealed record AgentMongoInsertRequest(string Database, string Collection, string DocumentEjson,
    int MaxTimeMs);

public sealed record AgentMongoUpdateRequest(string Database, string Collection, string IdEjson,
    string UpdateEjson, string ExpectedStateHash, int MaxTimeMs);

public sealed record AgentMongoDeleteRequest(string Database, string Collection, string IdEjson,
    string ExpectedStateHash, int MaxTimeMs);

public sealed record AgentMongoCreateIndexRequest(string Database, string Collection, string KeysEjson,
    string? Name, bool Unique, bool Sparse, int MaxTimeMs);

public sealed record AgentMongoDropIndexRequest(string Database, string Collection, string IndexName,
    string ExpectedStateHash, int MaxTimeMs);

public enum AgentMongoWriteStatus
{
    /// <summary>Server acknowledged the single intended effect.</summary>
    Applied = 1,
    /// <summary>Precondition no longer matches (document/index changed); nothing written.</summary>
    Conflict = 2,
    NotFound = 3,
    /// <summary>Rejected before sending (validation, forbidden operator/option, <c>_id_</c>, oversize).</summary>
    InvalidRequest = 4,
    /// <summary>Server denied (RBAC/authorization); nothing written.</summary>
    Forbidden = 5,
    /// <summary>Safe atomic precondition impossible for this target; nothing written.</summary>
    PreconditionUnavailable = 6,
    /// <summary>Command may have reached the server and the effect is unknown. Never replayed.</summary>
    OutcomeUnknown = 7,
    /// <summary>Failed before anything was sent (connection/target/deadline before dispatch).</summary>
    NotSent = 8,
}

/// <summary><see cref="ResultEjson"/> carries only canonical ids/names (inserted id, created index name).</summary>
public sealed record AgentMongoWriteResult(AgentMongoWriteStatus Status, long AffectedCount, string? ResultEjson,
    bool TargetVerified);
