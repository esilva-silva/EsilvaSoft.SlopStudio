using System.ComponentModel;
using System.Globalization;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>
/// Native agent chat of this tab (P7-L06-HOST). The chat owns its own session and cancellation; the tab only provides
/// a synchronous snapshot of its explicit destination and, when the editor showing it is realized, the selection. The
/// explorer selection is never read here.
/// </summary>
public sealed partial class WorkspaceTabViewModel
{
    private AgentChatViewModel? _agentChat;

    /// <summary>
    /// Supplied by the view currently showing this tab; returns the editor selection or null. It must be called on
    /// the UI thread and never outlive the view (the view guards it against showing another tab).
    /// </summary>
    public Func<string?>? EditorSelectionProvider { get; set; }

    /// <summary>Monotonic revision of the editor text (incremented on every change).</summary>
    public long EditorRevision => Interlocked.Read(ref _aiEditorRevision);

    /// <summary>This tab's chat, created on the first explicit opening of the agent panel; null before that.</summary>
    public AgentChatViewModel? AgentChat => _agentChat;

    /// <summary>
    /// Immutable view of this tab for the chat, captured synchronously on the UI thread before any await. Mirrors the
    /// visible tab context: in Console mode the collection belongs to the script, not to the tab destination.
    /// </summary>
    public AgentChatTabSnapshot CaptureAgentChatSnapshot()
    {
        string? selection;
        try
        {
            selection = EditorSelectionProvider?.Invoke();
        }
        catch (Exception)
        {
            selection = null; // A view in teardown shares nothing rather than failing the capture.
        }

        var database = string.IsNullOrWhiteSpace(Database) ? null : Database;
        var collection = IsConsole || database is null || string.IsNullOrWhiteSpace(Collection) ? null : Collection;
        return new AgentChatTabSnapshot(Id.ToString("N", CultureInfo.InvariantCulture), EditorRevision,
            Profile?.Id.ToString("D", CultureInfo.InvariantCulture), Profile?.Name, database, collection,
            string.IsNullOrEmpty(selection) ? null : selection);
    }

    internal void AttachAgentChat(AgentChatViewModel chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        if (_agentChat is not null)
        {
            throw new InvalidOperationException("A aba já possui um chat de agente.");
        }

        _agentChat = chat;
        PropertyChanged += OnAgentContextPropertyChanged;
        OnPropertyChanged(nameof(AgentChat));
    }

    /// <summary>Detaches the chat so the owner can dispose it (closing the tab ends its session and turn).</summary>
    internal AgentChatViewModel? DetachAgentChat()
    {
        var chat = _agentChat;
        if (chat is null)
        {
            return null;
        }

        PropertyChanged -= OnAgentContextPropertyChanged;
        _agentChat = null;
        EditorSelectionProvider = null;
        return chat;
    }

    /// <summary>
    /// Only an explicit change of this tab's own destination refreshes the chat's fixed context (and discards a
    /// reviewed package); selecting another node in the explorer never reaches this method.
    /// </summary>
    private void OnAgentContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Profile) or nameof(Database) or nameof(Collection) or nameof(Mode))
        {
            _agentChat?.RefreshTabContext();
        }
    }
}
