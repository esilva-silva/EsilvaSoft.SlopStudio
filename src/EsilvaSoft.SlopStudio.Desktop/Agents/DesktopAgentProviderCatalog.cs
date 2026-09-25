using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.Agents;

/// <summary>
/// Production, provider-neutral implementation of <see cref="IAgentProviderCatalog"/> for the Desktop composition
/// root (P7-L06-WIRING). It only wraps the shared <see cref="AgentProviderCatalog"/> already composed by
/// <c>AddSlopStudioInfrastructure</c>: every field it publishes comes from the descriptor/status contracts, never
/// from a provider ID comparison, so a brand-new registered provider (AC-09) is presented the same way as the
/// built-in ones without touching this type.
/// </summary>
/// <remarks>
/// <see cref="List"/> only reads its own cached snapshot, honoring the port's contract that listing must not touch
/// the vault or the network: construction seeds the snapshot with <see cref="AgentProviderStatus.NotReported"/> for
/// every registered provider. <see cref="RefreshAsync"/> is the explicit action — nothing calls it automatically —
/// that awaits <see cref="AgentProviderCatalog.GetStatusAsync"/> for each provider and swaps the snapshot atomically.
/// A provider whose status check fails is reported unavailable with the safe code that call already returns; this
/// type never branches on a provider ID to decide what to publish.
/// </remarks>
public sealed class DesktopAgentProviderCatalog : IAgentProviderCatalog
{
    private readonly AgentProviderCatalog _catalog;
    private volatile IReadOnlyList<AgentProviderPresentation> _snapshot;

    public DesktopAgentProviderCatalog(AgentProviderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _snapshot = Present(catalog.List(), _ => AgentProviderStatus.NotReported);
    }

    public IReadOnlyList<AgentProviderPresentation> List() => _snapshot;

    /// <summary>
    /// Refreshes the cached snapshot from the current local configuration and vault presence of every registered
    /// provider. <see cref="AgentProviderCatalog.GetStatusAsync"/> already fails closed per provider, so one
    /// unavailable/failing provider never affects the others; only the caller's own cancellation propagates.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var entries = _catalog.List();
        var statuses = new Dictionary<string, AgentProviderStatus>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            statuses[entry.Descriptor.ProviderId] =
                await _catalog.GetStatusAsync(entry.Descriptor.ProviderId, cancellationToken).ConfigureAwait(false);
        }

        _snapshot = Present(entries, id => statuses[id]);
    }

    private static IReadOnlyList<AgentProviderPresentation> Present(
        IReadOnlyList<AgentProviderEntry> entries, Func<string, AgentProviderStatus> statusOf) =>
        [.. entries.Select(entry =>
        {
            var status = statusOf(entry.Descriptor.ProviderId);
            return AgentProviderPresentation.From(entry, status, status.IsAvailable ? null : status.UnavailableCode);
        })];
}
