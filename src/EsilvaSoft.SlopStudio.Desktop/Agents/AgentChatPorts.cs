using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.Agents;

// Presentation ports of the native agent chat (phase 7, lote 6). Shared contracts were promoted on 24/09/2026:
// provider descriptor/capabilities/status (Core/Agents + IAgentProvider.Describe/GetStatusAsync + AgentProviderCatalog),
// trusted approval details (IAgentApprovalDetailsSource, AgentEvent.ApprovalExpiresAtUtc), sanitized tool destination
// (AgentEvent.ToolDestination) and API-key writes (IAgentApiKeyStore). What remains here is presentation only: a
// synchronous, localized view of the catalog and the tab snapshot.
//
// P7-L06-WIRING (25/09/2026): the composition root (App.axaml.cs) now registers the OpenAI/Claude adapters
// (Infrastructure.Agents) and a production IAgentProviderCatalog (DesktopAgentProviderCatalog, this folder), built
// only from IAgentRuntime/AgentProviderCatalog/capabilities — no branch by provider brand. ViewModels and Views keep
// depending only on these ports; only the composition root references Infrastructure.Agents, OpenAI, Anthropic or
// ModelContextProtocol (AC-04). IAgentApiKeyStore and IAgentApprovalDetailsSource still have no production
// implementation registered. The feature stays unavailable in the running IDE because AgentChatPanel/AgentChatServices
// are not wired into the main window yet — that hosting step needs PNG-inspected evidence in both themes and is out
// of this round's scope.

/// <summary>
/// Capability-oriented view of a registered provider. The chat reacts to these fields; it never branches on a provider
/// brand. <paramref name="UnavailableReason"/> is a safe, already localized sentence.
/// </summary>
public sealed record AgentProviderPresentation(
    string ProviderId,
    string DisplayName,
    AgentDataDestinationKind Destination,
    bool IsAvailable,
    IReadOnlyList<string> Models,
    IReadOnlyList<AgentAuthenticationMethod> AuthenticationMethods,
    AgentProviderAuthState AuthState,
    bool SupportsStreaming = true,
    bool SupportsToolCalling = false,
    string? UnavailableReason = null)
{
    /// <summary>
    /// Builds the view from promoted contracts. Destination comes from the catalog entry (provider <c>IsLocal</c>), the
    /// capabilities from the status already intersected with the descriptor; nothing is inferred from the brand.
    /// </summary>
    public static AgentProviderPresentation From(
        AgentProviderEntry entry, AgentProviderStatus status, string? localizedUnavailableReason = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(status);
        var descriptor = entry.Descriptor;
        return new AgentProviderPresentation(descriptor.ProviderId, descriptor.DisplayName, entry.Destination,
            status.IsAvailable, status.Models, descriptor.AuthenticationMethods, status.AuthState,
            status.Capabilities.Streaming, status.Capabilities.ToolCalling,
            status.IsAvailable ? null : localizedUnavailableReason);
    }
}

/// <summary>
/// Registered providers and their effective availability, as last reported. Listing must not start authentication,
/// read the vault or call the network; the implementation refreshes statuses through
/// <see cref="AgentProviderCatalog.GetStatusAsync"/> on explicit actions.
/// </summary>
public interface IAgentProviderCatalog
{
    IReadOnlyList<AgentProviderPresentation> List();
}

/// <summary>
/// Immutable state of the originating tab, captured synchronously on the UI thread. It comes from the tab's own
/// context (explicit destination), never from the explorer selection.
/// </summary>
public sealed record AgentChatTabSnapshot(
    string TabId,
    long DocumentVersion,
    string? ConnectionId,
    string? ConnectionLabel,
    string? Database,
    string? Collection,
    string? SelectedText);

/// <summary>What the user explicitly chose to share in a turn. Default is <see cref="None"/>.</summary>
public enum AgentContextScope
{
    /// <summary>Only the typed message.</summary>
    None,

    /// <summary>Logical connection ID and namespace names of the tab.</summary>
    Metadata,

    /// <summary>Namespace plus the editor selection confirmed in the preview.</summary>
    Selection,
}

/// <summary>Services consumed by the chat. Every member is optional: missing pieces make the feature unavailable.</summary>
public sealed record AgentChatServices(
    IAgentRuntime? Runtime,
    IAgentProviderCatalog? Catalog,
    IAgentContextProvider? ContextProvider,
    IAgentApprovalDetailsSource? ApprovalDetails = null,
    IAgentApiKeyStore? Credentials = null,
    TimeProvider? Time = null)
{
    public static AgentChatServices Unavailable { get; } = new(null, null, null);

    public bool IsComplete => Runtime is not null && Catalog is not null && ContextProvider is not null;

    public TimeProvider Clock => Time ?? TimeProvider.System;
}
