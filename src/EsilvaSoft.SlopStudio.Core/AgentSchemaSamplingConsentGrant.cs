namespace EsilvaSoft.SlopStudio.Core;

/// <summary>
/// Explicit local consent to sample document structure for schema derivation, scoped to one namespace and bound to
/// the exact profile generation it was granted for. A later regeneration of the same connection (reconnect,
/// credential rotation) does not carry this consent forward; it must be granted again.
/// </summary>
public sealed class AgentSchemaSamplingConsentGrant
{
    internal AgentSchemaSamplingConsentGrant(
        Guid principalId,
        Guid sourceGenerationId,
        AgentNamespaceScope scope,
        DateTimeOffset grantedAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        if (principalId == Guid.Empty)
            throw new ArgumentException("O consentimento precisa de um principal.", nameof(principalId));
        if (sourceGenerationId == Guid.Empty)
            throw new ArgumentException("O consentimento precisa da geração de origem do perfil.", nameof(sourceGenerationId));
        ArgumentNullException.ThrowIfNull(scope);
        if (expiresAtUtc is { } expires && expires <= grantedAtUtc)
            throw new ArgumentException("A expiração precisa ser posterior à concessão.", nameof(expiresAtUtc));
        PrincipalId = principalId;
        SourceGenerationId = sourceGenerationId;
        Scope = scope;
        GrantedAtUtc = grantedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid PrincipalId { get; }
    public Guid SourceGenerationId { get; }
    public AgentNamespaceScope Scope { get; }
    public DateTimeOffset GrantedAtUtc { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }

    public bool IsExpired(DateTimeOffset nowUtc) => ExpiresAtUtc is { } expires && nowUtc >= expires;
}
