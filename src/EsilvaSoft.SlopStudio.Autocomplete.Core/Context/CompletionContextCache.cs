using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Context;

/// <summary>
/// Small per-editor context cache. A context is immutable and keyed by the full captured request identity, so a
/// changed destination or cursor can never reuse semantic data from another request.
/// </summary>
public sealed class CompletionContextCache
{
    private readonly object _gate = new();
    private Entry? _last;

    public CompletionContextAnalysis Analyze(ContextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var key = ContextKey.From(request);
        lock (_gate)
            if (_last is { Key: var cached } entry && cached == key) return entry.Analysis;
        var analysis = CompletionContextEngine.Analyze(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _last = new(key, analysis);
        return analysis;
    }

    public void ClearDocument(long documentId)
    {
        lock (_gate)
            if (_last?.Key.Version.DocumentId == documentId) _last = null;
    }

    private sealed record Entry(ContextKey Key, CompletionContextAnalysis Analysis);

    private readonly record struct ContextKey(TextSnapshotVersion Version, int Caret, EditorDialects Dialect,
        CatalogScope? Scope, CompletionTrigger Trigger)
    {
        public static ContextKey From(ContextRequest request) => new(request.Snapshot.Version, request.ValidCaret, request.Dialect, request.TabScope, request.Trigger);
    }
}
