namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Typed recipient of tool output. Provider identifiers route an output grant and are not principal identity.</summary>
public sealed record AgentOutputDestination
{
    private AgentOutputDestination(AgentOutputDestinationKind kind, string? providerId)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == AgentOutputDestinationKind.Local && providerId is not null)
            throw new ArgumentException("O destino local não recebe identificador de provider.", nameof(providerId));
        if (kind == AgentOutputDestinationKind.ProviderExternal &&
            (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 64 || providerId.Any(char.IsControl)))
            throw new ArgumentException("O destino externo exige um identificador de provider válido.", nameof(providerId));
        Kind = kind;
        ProviderId = providerId;
    }

    public AgentOutputDestinationKind Kind { get; }
    public string? ProviderId { get; }

    internal static AgentOutputDestination Local() => new(AgentOutputDestinationKind.Local, null);
    internal static AgentOutputDestination ProviderExternal(string providerId) => new(AgentOutputDestinationKind.ProviderExternal, providerId);
}
