using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Context;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    public IAutocompleteService Autocomplete { get; set; } = new AutocompleteService();
    /// <summary>Assigned by the workspace composition root; null keeps isolated design-time tabs functional.</summary>
    public ICompletionProvider? TraditionalCompletion { get; set; }
    /// <summary>
    /// Assigned by the workspace composition root. Reads the pipeline input shape without I/O (<see cref="MetadataAccess.Peek"/>).
    /// Must return the same cached instance while the underlying schema is unchanged: <see cref="CompletionContextCache"/>
    /// compares it by reference, so a freshly built instance on every call would defeat the context cache.
    /// </summary>
    public Func<CatalogScope, CollectionSchema?>? PipelineInputSchema { get; set; }
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
    /// <summary>Owns cancellation of the traditional list request for this tab only; never shared with another tab or view.</summary>
    private readonly EditorRequestScope _traditionalRequestScope = new();
    private string _traditionalSnapshotText = "";
    private long _traditionalSnapshotSequence;
    private long _traditionalCompletionRequestId;

    /// <summary>Cancels the traditional list request in flight for this tab, if any. Safe to call when none is pending.</summary>
    public void CancelTraditionalCompletion() => _traditionalRequestScope.Cancel();

    public async Task<TraditionalCompletionResult> GetTraditionalCompletionsAsync(string text, int caret, CancellationToken token, CompletionTrigger trigger = CompletionTrigger.Invoked)
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

        return await GetTraditionalCompletionsAsync(new StringTextSnapshot(text, version), caret, token, trigger);
    }

    public async Task<TraditionalCompletionResult> GetTraditionalCompletionsAsync(ITextSnapshot snapshot, int caret, CancellationToken token, CompletionTrigger trigger = CompletionTrigger.Invoked)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var provider = TraditionalCompletion;
        if (provider is null) return new([], false);
        var profile = Profile;
        var scope = profile is null ? null : new CatalogScope(ConnectionIdentity.From(profile), Database, Collection);
        var dialect = Mode switch { "Agregação" => EditorDialects.AggregationJson, "Script" => EditorDialects.MongoshScript, _ => EditorDialects.Console };
        var capturedCaret = Math.Clamp(caret, 0, snapshot.Length);
        // Captured before the await: PipelineInputSchema reads the metadata cache with Peek, never scheduling I/O.
        var inputSchema = scope is null ? null : PipelineInputSchema?.Invoke(scope);
        var requestId = Interlocked.Increment(ref _traditionalCompletionRequestId);
        var response = await Task.Run(async () =>
        {
            // Analysis still runs under the caller's token: the lease only exists once the CompletionRequest is built,
            // so an earlier analysis in this tab has no other way to be cancelled by a newer one.
            var context = _traditionalContextCache.Analyze(
                new ContextRequest(snapshot, capturedCaret, dialect, scope, trigger) { InputSchema = inputSchema }, token).Context;
            var request = new CompletionRequest(context, requestId, 0);
            var lease = _traditionalRequestScope.Begin(request);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.CancellationToken);
            var result = await provider.CompleteAsync(request, linked.Token);
            // A concurrent, newer invocation of this same tab superseded this one; discard even if the provider
            // ignored cancellation and still returned a response (CompletionResponse.IsFor as the explicit guard).
            if (!lease.IsCurrent || !result.IsFor(request))
                throw new OperationCanceledException("Uma solicitação de sugestões mais recente desta aba substituiu esta resposta.");
            return result;
        }, token);
        return new(response.List.Items, response.List.IsIncomplete, response.Request.Context);
    }

    /// <summary>
    /// Sinal de uso da sessão, fornecido pelo workspace e compartilhado entre abas. Nulo em abas isoladas de projeto
    /// ou de teste, caso em que o aceite simplesmente não registra nada.
    /// </summary>
    public CompletionUsageTracker? CompletionUsage { get; set; }

    /// <summary>
    /// Registra que uma sugestão foi aceita. Sem conexão, coleção ou forma resolvidas a chave não é construível e o
    /// aceite não gera sinal algum — nunca uma chave sintética, que misturaria estatística de posições diferentes.
    /// </summary>
    public void RecordCompletionAccepted(CompletionContext? context, string symbolId)
    {
        if (CompletionUsage is not { } tracker || context is null) return;
        if (CompletionRanker.TryCreateUsageKey(context, symbolId, out var key)) tracker.RecordAccepted(key);
    }

    /// <summary>
    /// Registra que o aceite foi desfeito. O próprio rastreador decide se o undo veio dentro da janela curta que
    /// caracteriza arrependimento; fora dela a chamada não tem efeito.
    /// </summary>
    public void RecordCompletionUndone(CompletionContext? context, string symbolId)
    {
        if (CompletionUsage is not { } tracker || context is null) return;
        if (CompletionRanker.TryCreateUsageKey(context, symbolId, out var key)) tracker.RecordUndone(key);
    }

    /// <summary>Releases this tab's own cancellation scope; never touches another tab's requests.</summary>
    public void Dispose() => _traditionalRequestScope.Dispose();
}
