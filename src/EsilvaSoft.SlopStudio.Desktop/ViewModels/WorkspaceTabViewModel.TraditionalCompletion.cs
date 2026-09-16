using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Language;
using EsilvaSoft.SlopStudio.Application.Language.Completion;
using EsilvaSoft.SlopStudio.Application.Language.Context;
using EsilvaSoft.SlopStudio.Application.Language.Text;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    public IAutocompleteService Autocomplete { get; set; } = new AutocompleteService();
    /// <summary>Assigned by the workspace composition root; null keeps isolated design-time tabs functional.</summary>
    public ICompletionProvider? TraditionalCompletion { get; set; }
    /// <summary>Effective persisted shortcuts, supplied by the workspace that owns this tab.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> KeyBindings { get; set; } = EditorKeyBindings.Resolve(null);
    /// <summary>Raised only when a metadata cache change can affect this tab's captured completion scope.</summary>
    public event EventHandler? TraditionalCompletionRefreshRequested;

    public void NotifyMetadataChanged(MetadataChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var profile = Profile;
        if (profile is null || profile.Id != change.ProfileId) return;
        if (change.Key is { } key)
        {
            if (key.Connection.ProfileId != profile.Id) return;
            if (key.Database.Length > 0 && !string.Equals(key.Database, Database, StringComparison.Ordinal)) return;
            if (key.Collection.Length > 0 && Collection.Length > 0 && !string.Equals(key.Collection, Collection, StringComparison.Ordinal)) return;
        }
        TraditionalCompletionRefreshRequested?.Invoke(this, EventArgs.Empty);
    }
    private static long _traditionalDocumentId;
    private readonly long _traditionalCompletionDocumentId = Interlocked.Increment(ref _traditionalDocumentId);
    private readonly object _traditionalContextGate = new();
    private readonly CompletionContextCache _traditionalContextCache = new();
    private string _traditionalSnapshotText = "";
    private long _traditionalSnapshotSequence;
    private long _traditionalCompletionRequestId;

    public async Task<TraditionalCompletionResult> GetTraditionalCompletionsAsync(string text, int caret, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text);
        TextSnapshotVersion version;
        lock (_traditionalContextGate)
        {
            if (!string.Equals(_traditionalSnapshotText, text, StringComparison.Ordinal))
            {
                _traditionalSnapshotText = text;
                _traditionalSnapshotSequence++;
            }
            version = new TextSnapshotVersion(_traditionalCompletionDocumentId, _traditionalSnapshotSequence);
        }

        return await GetTraditionalCompletionsAsync(new StringTextSnapshot(text, version), caret, token);
    }

    public async Task<TraditionalCompletionResult> GetTraditionalCompletionsAsync(ITextSnapshot snapshot, int caret, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var provider = TraditionalCompletion;
        if (provider is null) return new([], false);
        var profile = Profile;
        var scope = profile is null ? null : new CatalogScope(ConnectionIdentity.From(profile), Database, Collection);
        var dialect = Mode switch { "Agregação" => EditorDialects.AggregationJson, "Script" => EditorDialects.MongoshScript, _ => EditorDialects.Console };
        var requestId = Interlocked.Increment(ref _traditionalCompletionRequestId);
        var capturedCaret = Math.Clamp(caret, 0, snapshot.Length);
        var response = await Task.Run(async () =>
        {
            var context = _traditionalContextCache.Analyze(new ContextRequest(snapshot, capturedCaret, dialect, scope, CompletionTrigger.Invoked), token).Context;
            return await provider.CompleteAsync(new CompletionRequest(context, requestId, 0), token);
        }, token);
        return new(response.List.Items, response.List.IsIncomplete);
    }
}
