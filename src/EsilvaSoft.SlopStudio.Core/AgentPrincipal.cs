namespace EsilvaSoft.SlopStudio.Core;

/// <summary>
/// Authenticated authorization subject. It has no public constructor and must be issued from trusted runtime context,
/// never from model/tool arguments. Provider, client, session and turn identifiers belong to invocation context,
/// not to the MongoDB grant subject.
/// </summary>
public sealed class AgentPrincipal
{
    internal AgentPrincipal(Guid id, AgentPrincipalOrigin origin, long policyRevision)
    {
        if (id == Guid.Empty) throw new ArgumentException("O principal precisa ter um identificador.", nameof(id));
        if (!Enum.IsDefined(origin)) throw new ArgumentOutOfRangeException(nameof(origin));
        ArgumentOutOfRangeException.ThrowIfLessThan(policyRevision, 1);
        Id = id;
        Origin = origin;
        PolicyRevision = policyRevision;
    }

    /// <summary>Opaque grant subject identifier, unrelated to provider, client, session or turn identifiers.</summary>
    public Guid Id { get; }
    public AgentPrincipalOrigin Origin { get; }
    public long PolicyRevision { get; }
}
