using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>
/// Hosting of the native agent chat in the main window (P7-L06-HOST). The panel is collapsed at startup and the chat
/// services are requested only when the user opens it; each workspace tab owns one <see cref="AgentChatViewModel"/>
/// (session, turn and cancellation included), created lazily. The panel always shows the chat of the active tab, and
/// the chat context comes only from that tab's snapshot — never from the explorer selection. Without composed services
/// the panel shows the unavailable state while the rest of the IDE keeps working.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private readonly AgentChatServicesFactory? _agentChatServices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveAgentChat))]
    private bool _isAgentPanelOpen;

    /// <summary>Chat shown by the panel: the active tab's, only while the panel is open.</summary>
    public AgentChatViewModel? ActiveAgentChat => IsAgentPanelOpen ? ActiveTab?.AgentChat : null;

    /// <summary>Whether the composition root supplied chat services (false in hosts without the agent platform).</summary>
    public bool IsAgentPlatformComposed => _agentChatServices is not null;

    [RelayCommand]
    private void ToggleAgentPanel() => IsAgentPanelOpen = !IsAgentPanelOpen;

    partial void OnIsAgentPanelOpenChanged(bool value)
    {
        if (value)
        {
            EnsureAgentChat(ActiveTab);
        }

        OnPropertyChanged(nameof(ActiveAgentChat));
    }

    private void OnActiveTabChangedForAgent(WorkspaceTabViewModel? tab)
    {
        if (IsAgentPanelOpen)
        {
            EnsureAgentChat(tab);
        }

        OnPropertyChanged(nameof(ActiveAgentChat));
    }

    private void EnsureAgentChat(WorkspaceTabViewModel? tab)
    {
        if (tab is null || _disposed)
        {
            return;
        }

        if (tab.AgentChat is { } existing)
        {
            // Another tab may have re-checked the shared catalog: pick up the cached listing (no vault, no network).
            existing.ReloadProvidersIfChanged();
            return;
        }

        var services = _agentChatServices?.GetServices() ?? AgentChatServices.Unavailable;
        tab.AttachAgentChat(new AgentChatViewModel(services, tab.CaptureAgentChatSnapshot, () => WorkspaceRootPath));
    }

    /// <summary>
    /// The Files panel folder is the read scope of CLI-delegated providers in new sessions: every open chat refreshes
    /// its permanent read notice (text only; running sessions keep the folder fixed at their creation).
    /// </summary>
    private void RefreshAgentReadScopes()
    {
        foreach (var tab in Tabs)
        {
            tab.AgentChat?.RefreshReadScope();
        }
    }

    /// <summary>Closing a tab ends its chat: the running turn is cancelled and its session closed.</summary>
    private static void ReleaseAgentChat(WorkspaceTabViewModel tab)
    {
        if (tab.DetachAgentChat() is { } chat)
        {
            _ = DisposeAgentChatAsync(chat);
        }
    }

    private static async Task DisposeAgentChatAsync(AgentChatViewModel chat)
    {
        try
        {
            await chat.DisposeAsync();
        }
        catch (Exception)
        {
            // Closing is best effort; the runtime disposes sessions that do not confirm shutdown.
        }
    }
}
