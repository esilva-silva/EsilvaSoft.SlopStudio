using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Language;
using EsilvaSoft.SlopStudio.Application.Language.Completion;
using EsilvaSoft.SlopStudio.Application.Language.Context;
using EsilvaSoft.SlopStudio.Application.Language.Text;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public enum ResultViewMode { Json, Tree }

/// <summary>Character range of one document inside the JSON view text.</summary>
public sealed record ResultTextSegment(int Start, int Length, ResultDocumentViewModel Document);

/// <summary>Whether a result document can be opened as an editable copy, and why not.</summary>
public sealed record ResultEditAvailability(bool CanOpen, string Reason);
public sealed record TraditionalCompletionResult(IReadOnlyList<CompletionItem> Items, bool IsIncomplete);

public sealed partial class WorkspaceTabViewModel : ObservableObject
{
    private const string NotExecutedText = "Execute para visualizar os resultados em Extended JSON.";
    private readonly WorkspaceService _workspace;
    public IApplicationOperationService Operations => _workspace.Operations;
    public Task ExportResultPageAsync(string path, IReadOnlyList<string> documents, bool csv, Action<int, int> progress, CancellationToken cancellationToken) =>
        _workspace.ExportResultPageAsync(path, documents, csv, progress, cancellationToken);
    public Task<string> FormatCodeAsync(string text, CancellationToken cancellationToken) => _workspace.FormatCodeAsync(text, cancellationToken);
    public Task<CodeValidationResult> ValidateCodeAsync(string text, bool aggregation, CancellationToken token) => _workspace.ValidateCodeAsync(text, aggregation, token);
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
    private CancellationTokenSource? _cancellation;
    private bool _restoring;
    private CancellationTokenSource? _presentationCancellation;
    private string _savedText = "";
    private int _historyGeneration;
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? MissingProfileId { get; private set; }
    public string? MissingTargetHost { get; private set; }
    public bool ContainsResultData { get; set; }
    public event EventHandler? DraftChanged;
    public IReadOnlyList<string> Modes { get; } = ["Console", "Script", "Agregação"];
    public Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> ConfirmConsoleWrite { get; set; } = (_, _) => Task.FromResult(false);
    public ObservableCollection<ConsoleHistoryEntry> ConsoleHistory { get; } = [];
    public ObservableCollection<ConsoleResultSet> ConsoleResults { get; } = [];
    [ObservableProperty] private ConsoleResultSet? _selectedConsoleResult;
    [ObservableProperty] private ConsoleHistoryEntry? _selectedConsoleHistory;
    partial void OnSelectedConsoleResultChanged(ConsoleResultSet? value)
    {
        if (_resultIsConsole) ShowSetDocuments(value is not null && _consoleSets.TryGetValue(value, out var set) ? set : null);
    }
    public bool IsConsole => Mode == "Console";
    public ConsoleStatement GetConsoleStatement(int caret) => _workspace.GetConsoleStatement(Text, caret);
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
    public ObservableCollection<MqlSuggestion> Suggestions { get; } = [];
    public ObservableCollection<QueryHistoryEntry> History { get; } = [];
    public ObservableCollection<SavedQuery> SavedQueries { get; } = [];
    public ObservableCollection<ScriptHistoryEntry> ScriptHistory { get; } = [];

    [ObservableProperty] private ConnectionProfile? _profile;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _database = "";
    [ObservableProperty] private string _collection = "";
    [ObservableProperty] private string _mode = "Console";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _inputJson = "{}";
    [ObservableProperty] private bool _persistInput;
    [ObservableProperty] private bool _historyEnabled = true;
    [ObservableProperty] private bool _scriptHistoryEnabled = true;
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _results = NotExecutedText;
    [ObservableProperty] private string _messages = "";
    [ObservableProperty] private string _errors = "";
    [ObservableProperty] private string _status = "Pronto";
    [ObservableProperty] private string _metrics = "";
    [ObservableProperty] private int _resultTabIndex;
    [ObservableProperty] private double _codeFontSize = 14;
    [ObservableProperty] private string _projection = "";
    [ObservableProperty] private string _sort = "";
    [ObservableProperty] private int _limit = 100;
    [ObservableProperty] private int _skip;
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _comment = "";
    [ObservableProperty] private string _collation = "";
    [ObservableProperty] private int _batchSize = 100;
    [ObservableProperty] private int _maxTimeMs = 30000;
    [ObservableProperty] private string _savedQueryName = "";
    [ObservableProperty] private bool _savedQueryIsFavorite;
    [ObservableProperty] private QueryHistoryEntry? _selectedHistory;
    [ObservableProperty] private SavedQuery? _selectedSavedQuery;
    [ObservableProperty] private ScriptHistoryEntry? _selectedScriptHistory;
    [ObservableProperty] private MqlSuggestion? _selectedSuggestion;

    // Results live only in memory for this tab: Snapshot() never includes them; a new execution or destination change discards them.
    public ObservableCollection<ResultDocumentViewModel> ResultDocuments { get; } = [];
    [ObservableProperty] private ResultDocumentViewModel? _selectedDocument;
    public ObservableCollection<ResultNodeViewModel> ResultTree { get; } = [];
    public IReadOnlyList<ResultTextSegment> ResultSegments { get; private set; } = [];
    [ObservableProperty] private ResultViewMode _resultView = ResultViewMode.Json;
    [ObservableProperty] private ResultNodeViewModel? _selectedResultNode;
    [ObservableProperty] private string _resultTreeStatus = NotExecutedText;
    private StructuredResultSet[] _resultSets = [];
    private readonly Dictionary<StructuredResultSet, IReadOnlyList<ResultDocumentViewModel>> _documentViews = [];
    private readonly Dictionary<ConsoleResultSet, StructuredResultSet> _consoleSets = new(ReferenceEqualityComparer.Instance);
    private ResultTreeState _treeState = new();
    private ResultDocumentViewModel? _pendingSelection;
    private bool _syncingSelection;
    private IReadOnlyList<string> _documents = [];
    // Canonical Extended JSON remains the source for identity, editing and export; ObjectId/UUID constructors are presentation only.
    [ObservableProperty] private UuidDisplayPolicy _uuidPolicy = UuidDisplayPolicy.Default;
    private UuidDisplayPolicy _resultPolicy = UuidDisplayPolicy.Default;
    private Guid? _resultProfileId;
    private bool _resultIsConsole;
    private bool _resultRenderable;
    private string? _resultEmptyText;
    private string _resultMetrics = "";

    public IReadOnlyList<StructuredResultSet> ResultSets => _resultSets;
    /// <summary>Extended JSON of the selected result set, used by export and field inference.</summary>
    public IReadOnlyList<string> Documents
    {
        get => _documents;
        private set
        {
            _documents = value;
            OnPropertyChanged(nameof(CanExport));
        }
    }
    public bool IsJsonResultView { get => ResultView == ResultViewMode.Json; set => ChooseResultView(value, ResultViewMode.Json); }
    public bool IsTreeResultView { get => ResultView == ResultViewMode.Tree; set => ChooseResultView(value, ResultViewMode.Tree); }
    public bool HasResultTree => ResultTree.Count > 0;
    public bool HasSelectedDocument => SelectedDocument is not null;
    public string CopyJsonHint => SelectedDocument is { } document
        ? "Copiar o JSON formatado de " + document.Summary
        : "Selecione um documento nos resultados para copiar.";

    private void ChooseResultView(bool chosen, ResultViewMode mode)
    {
        if (!chosen) return;
        ResultView = mode;
        ResultTabIndex = 0;
    }

    partial void OnResultViewChanged(ResultViewMode value)
    {
        OnPropertyChanged(nameof(IsJsonResultView));
        OnPropertyChanged(nameof(IsTreeResultView));
        SyncTreeSelection();
    }

    partial void OnSelectedDocumentChanged(ResultDocumentViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedDocument));
        OnPropertyChanged(nameof(CopyJsonHint));
        if (!_syncingSelection) SyncTreeSelection();
    }

    partial void OnSelectedResultNodeChanged(ResultNodeViewModel? value)
    {
        if (_syncingSelection || value?.Document is not { } document || ReferenceEquals(document, SelectedDocument)) return;
        var previous = _syncingSelection;
        _syncingSelection = true;
        try { SelectResultDocumentCore(document); }
        finally { _syncingSelection = previous; }
    }

    /// <summary>Selects a document from any view; in the Console the owning result set becomes the selected set.</summary>
    public void SelectResultDocument(ResultDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!ReferenceEquals(document, SelectedDocument)) SelectResultDocumentCore(document);
    }

    private void SelectResultDocumentCore(ResultDocumentViewModel document)
    {
        if (_resultIsConsole && ConsoleFor(document.Document.Set) is { } console && !ReferenceEquals(console, SelectedConsoleResult))
        {
            _pendingSelection = document;
            try { SelectedConsoleResult = console; }
            finally { _pendingSelection = null; }
        }
        SelectedDocument = document;
    }

    /// <summary>Opening an editable copy reads nothing; availability depends only on the captured result.</summary>
    public static ResultEditAvailability GetEditAvailability(ResultDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var set = document.Document.Set;
        if (!document.Document.IsValid) return new(false, "JSON inválido: não há documento para editar.");
        if (!set.Origin.HasCollection) return new(false, "Resultado sem coleção de origem conhecida. Consulte a coleção no Console para editar.");
        if (document.IdentityFilter is null) return new(false, "Documento sem _id: não há identidade segura para gravar. Refaça a consulta sem excluir _id.");
        return set.Completeness switch
        {
            ResultCompleteness.PartialProjection => new(false, "Projeção parcial: a cópia não contém o documento completo. Execute a consulta sem projeção para editar."),
            ResultCompleteness.Derived => new(false, "Resultado de agregação: pode não corresponder a um documento armazenado. Use find para editar."),
            ResultCompleteness.Unknown => new(false, "Origem sem identidade segura. Use find na coleção para editar."),
            _ => new(true, "")
        };
    }

    /// <summary>
    /// Opens the copy held in memory without querying or writing. Saving is an explicit, confirmed action that reads the
    /// document again by <c>_id</c> and still uses the <c>$$ROOT</c> precondition.
    /// </summary>
    public DocumentMutationViewModel CreateResultDocumentEditor(ResultDocumentViewModel document)
    {
        var availability = GetEditAvailability(document);
        if (!availability.CanOpen) throw new InvalidOperationException(availability.Reason);
        var origin = document.Document.Set.Origin;
        return new(_workspace, origin.Profile!, origin.Database!, origin.Collection!, document, "Editar", rereadBeforeWrite: true,
            writeBlockReason: () => IsRunning ? "Aguarde a execução desta aba terminar." : !IsConnected ? "Conexão desta aba fechada: abra a conexão antes de salvar." : null);
    }

    private void ShowSetDocuments(StructuredResultSet? set)
    {
        IReadOnlyList<ResultDocumentViewModel> views = set is not null && _documentViews.TryGetValue(set, out var list) ? list : [];
        Documents = set?.Documents?.Select(document => document.Json).ToArray() ?? [];
        var preferred = _pendingSelection is { } pending && views.Contains(pending) ? pending
            : SelectedDocument is { } current && views.Contains(current) ? current : views.Count > 0 ? views[0] : null;
        if (!ResultDocuments.SequenceEqual(views))
        {
            var previous = _syncingSelection;
            _syncingSelection = true;
            try
            {
                ResultDocuments.Clear();
                foreach (var view in views) ResultDocuments.Add(view);
            }
            finally { _syncingSelection = previous; }
        }
        SelectedDocument = preferred;
        if (!_syncingSelection) SyncTreeSelection();
    }

    private void SyncTreeSelection()
    {
        if (SelectedResultNode?.Document is { } current && ReferenceEquals(current, SelectedDocument)) return;
        var previous = _syncingSelection;
        _syncingSelection = true;
        try { SelectedResultNode = SelectedDocument is { } document ? FindDocumentNode(document) : null; }
        finally { _syncingSelection = previous; }
    }

    private ResultNodeViewModel? FindDocumentNode(ResultDocumentViewModel document)
    {
        if (_treeState.DocumentNodes.TryGetValue(document, out var node)) return node;
        var key = "s" + document.Document.Set.Number.ToString(CultureInfo.InvariantCulture);
        if (ResultTree.FirstOrDefault(n => n.Kind == ResultNodeKind.ResultSet && n.Key == key) is not { } set) return null;
        set.IsExpanded = true;
        return _treeState.DocumentNodes.GetValueOrDefault(document);
    }

    private ConsoleResultSet? ConsoleFor(StructuredResultSet set) => _consoleSets.FirstOrDefault(pair => ReferenceEquals(pair.Value, set)).Key;

    partial void OnUuidPolicyChanged(UuidDisplayPolicy value) => ApplyPendingUuidPolicy();
    private void ApplyPendingUuidPolicy()
    {
        // An execution renders with the policy captured at its start; a later preference is applied from canonical data once idle.
        if (IsRunning || !_resultRenderable || ReferenceEquals(_resultPolicy, UuidPolicy)) return;
        _resultPolicy = UuidPolicy;
        RenderResults();
    }

    private async Task SetResultsAsync(Func<StructuredResultSet[]> createSets, IReadOnlyList<ConsoleResultSet>? console, CancellationToken token)
    {
        var policy = _resultPolicy; var profileId = _resultProfileId; var isConsole = _resultIsConsole;
        var prepared = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var sets = createSets();
            return (Sets: sets, Presentation: PrepareResults(sets, policy, profileId, isConsole, token));
        }, token);
        token.ThrowIfCancellationRequested();
        _resultSets = prepared.Sets;
        _consoleSets.Clear();
        if (console is not null)
            for (var i = 0; i < console.Count; i++) _consoleSets[console[i]] = _resultSets[i];
        ConsoleResults.Clear();
        if (console is not null)
            foreach (var set in console) ConsoleResults.Add(set);
        _resultRenderable = true;
        ApplyPresentation(prepared.Presentation);
    }

    private void ClearResults(string state)
    {
        _presentationCancellation?.Cancel();
        _resultSets = [];
        _consoleSets.Clear();
        _documentViews.Clear();
        _treeState = new();
        ConsoleResults.Clear();
        SelectedConsoleResult = null;
        var previous = _syncingSelection;
        _syncingSelection = true;
        try
        {
            SelectedResultNode = null;
            SelectedDocument = null;
            ResultTree.Clear();
            ResultDocuments.Clear();
        }
        finally { _syncingSelection = previous; }
        ResultSegments = [];
        Documents = [];
        SetResultState(state);
        OnPropertyChanged(nameof(HasResultTree));
    }

    private void SetResultState(string text)
    {
        Results = text;
        ResultTreeStatus = text;
    }

    private sealed record ResultPresentation(Dictionary<StructuredResultSet, IReadOnlyList<ResultDocumentViewModel>> Views,
        IReadOnlyList<ResultTextSegment> Segments, string Text, int Unknown, UuidRepresentation Primary);

    private static ResultPresentation PrepareResults(StructuredResultSet[] sets, UuidDisplayPolicy policy, Guid? profileId, bool isConsole, CancellationToken token)
    {
        var documentViews = new Dictionary<StructuredResultSet, IReadOnlyList<ResultDocumentViewModel>>();
        var primary = policy.Resolve(profileId);
        var unknown = 0;
        var text = new StringBuilder();
        var segments = new List<ResultTextSegment>();
        foreach (var set in sets)
        {
            var representation = policy.ResolveOptions(set.Origin.ProfileId ?? profileId);
            if (text.Length > 0) text.Append("\n\n");
            if (isConsole) text.Append("// ").Append(SetHeader(set, representation.Uuid == primary ? null : representation.Uuid)).Append('\n');
            if (set.Documents is { } documents)
            {
                var views = documents.Select(document => { token.ThrowIfCancellationRequested(); return new ResultDocumentViewModel(document, representation); }).ToArray();
                documentViews[set] = views;
                if (views.Length == 0 && isConsole) text.Append("// Nenhum documento neste resultado.");
                for (var i = 0; i < views.Length; i++)
                {
                    var view = views[i];
                    if (i > 0) text.Append("\n\n");
                    if (!view.Document.IsValid) text.Append("// ").Append(view.Label).Append(": JSON inválido — ").Append(view.Document.InvalidJsonMessage).Append('\n');
                    unknown += view.UnknownLegacyUuidCount;
                    segments.Add(new(text.Length, view.FormattedJson.Length, view));
                    text.Append(view.FormattedJson);
                }
            }
            else
            {
                var display = IdentifierRepresentationService.FormatForDisplay(ExtendedJsonFormatter.TryFormat(set.Json, out var formatted, out _) ? formatted : set.Json, representation);
                unknown += display.UnknownLegacyCount;
                text.Append(display.Text);
            }
        }
        token.ThrowIfCancellationRequested();
        return new(documentViews, segments, text.ToString(), unknown, primary);
    }

    private void RenderResults()
    {
        _presentationCancellation?.Cancel();
        if (_resultSets.Sum(set => set.Documents?.Sum(document => (long)document.Json.Length) ?? set.Json.Length) <= 65536)
            ApplyPresentation(PrepareResults(_resultSets, _resultPolicy, _resultProfileId, _resultIsConsole, CancellationToken.None));
        else _ = RenderLargeResultsAsync();
    }

    private async Task RenderLargeResultsAsync()
    {
        var sets = _resultSets; var policy = _resultPolicy; var profileId = _resultProfileId; var isConsole = _resultIsConsole;
        using var operation = Operations.Begin("Atualizando apresentação BSON", ApplicationOperationPriority.Normal);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _presentationCancellation = cancellation;
        try
        {
            var presentation = await Task.Run(() => PrepareResults(sets, policy, profileId, isConsole, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(sets, _resultSets) || !ReferenceEquals(policy, _resultPolicy)) return;
            ApplyPresentation(presentation);
            operation.Complete(ApplicationOperationStatus.Success, "Apresentação BSON atualizada");
        }
        catch (OperationCanceledException) { operation.Complete(ApplicationOperationStatus.Cancelled, "Atualização da apresentação cancelada"); }
        catch (Exception ex) { Errors = OperationErrorMessages.Describe(ex); operation.Complete(ApplicationOperationStatus.Error, "Falha na apresentação BSON"); }
        finally { if (ReferenceEquals(_presentationCancellation, cancellation)) _presentationCancellation = null; }
    }

    private void ApplyPresentation(ResultPresentation presentation)
    {
        var previous = SelectedDocument?.Document is { } selected ? (selected.Set.Number, selected.Position) : ((int Set, int Position)?)null;
        _documentViews.Clear();
        foreach (var pair in presentation.Views) _documentViews.Add(pair.Key, pair.Value);
        ResultSegments = presentation.Segments;
        Results = _resultEmptyText ?? presentation.Text;
        Metrics = _resultMetrics + " · IDs " + IdentifierRepresentationService.DisplayName(_resultPolicy.Mode) + " · UUID " + UuidCodec.DisplayName(presentation.Primary)
            + (presentation.Unknown == 0 ? "" : $" · {presentation.Unknown} UUID(s) legado(s) de origem desconhecida");
        BuildResultTree();
        var target = previous is { } key ? _documentViews.FirstOrDefault(pair => pair.Key.Number == key.Set).Value?.ElementAtOrDefault(key.Position) : null;
        RestoreSelection(target);
    }

    private static string SetHeader(StructuredResultSet set, UuidRepresentation? distinctRepresentation) =>
        set.Label + (set.Method is null ? "" : " · " + set.Method)
        + (set.Documents is { } documents ? " · " + documents.Count.ToString(CultureInfo.InvariantCulture) + " documento(s)" : "")
        + (set.IsTruncated ? " · limitado" : "")
        + (set.Completeness == ResultCompleteness.PartialProjection ? " · projeção parcial" : "")
        + (distinctRepresentation is { } representation ? " · UUID " + UuidCodec.DisplayName(representation) : "");

    private void BuildResultTree()
    {
        var previous = _syncingSelection;
        _syncingSelection = true;
        try
        {
            SelectedResultNode = null;
            ResultTree.Clear();
        }
        finally { _syncingSelection = previous; }
        _treeState.DocumentNodes.Clear();
        foreach (var set in _resultSets)
        {
            var representation = _resultPolicy.ResolveOptions(set.Origin.ProfileId ?? _resultProfileId);
            var views = _documentViews.GetValueOrDefault(set);
            if (_resultIsConsole) ResultTree.Add(ResultNodeViewModel.ForSet(set, views, representation, _treeState));
            else foreach (var node in ResultNodeViewModel.SetContent(set, views ?? [], representation, _treeState)) ResultTree.Add(node);
        }
        if (!_treeState.Rendered)
        {
            _treeState.Rendered = true;
            // A new result opens the first set with documents, and its document when it is the only one.
            var first = _resultIsConsole ? ResultTree.FirstOrDefault(node => node.DocumentCount > 0) : null;
            if (first is not null) first.IsExpanded = true;
            var documents = (_resultIsConsole ? first?.Children ?? [] : ResultTree).Where(node => node.IsDocument).ToArray();
            if (documents.Length == 1) documents[0].IsExpanded = true;
        }
        ResultTreeStatus = ResultTree.Count == 0 ? _resultEmptyText ?? "Nenhum resultado." : "";
        OnPropertyChanged(nameof(HasResultTree));
    }

    private void RestoreSelection(ResultDocumentViewModel? target)
    {
        _pendingSelection = target;
        try
        {
            if (_resultIsConsole)
            {
                var console = target is not null ? ConsoleFor(target.Document.Set)
                    : SelectedConsoleResult is { } current && ConsoleResults.Contains(current) ? current
                    : ConsoleResults.FirstOrDefault(set => set.Documents is { Count: > 0 });
                if (ReferenceEquals(console, SelectedConsoleResult)) ShowSetDocuments(console is null ? null : _consoleSets[console]);
                else SelectedConsoleResult = console;
            }
            else ShowSetDocuments(_resultSets.Length > 0 ? _resultSets[0] : null);
        }
        finally { _pendingSelection = null; }
        SyncTreeSelection();
    }

    public async Task<DocumentMutationViewModel> CreateDocumentMutationAsync(string operation)
    {
        if (Profile is null || !IsConnected || IsRunning) throw new InvalidOperationException("Abra uma coleção conectada e aguarde a consulta.");
        var profile = IsConsole ? SelectedConsoleResult?.SourceProfile : Profile;
        var database = IsConsole ? SelectedConsoleResult?.Database : Database;
        var collection = IsConsole ? SelectedConsoleResult?.Collection : Collection;
        if (profile is null || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(collection)) throw new InvalidOperationException("Selecione um resultado de coleção antes de editar documentos.");
        var selected = SelectedDocument;
        var representation = UuidPolicy.ResolveOptions(profile.Id);
        profile.EnsureWriteAllowed();
        if (operation == "Inserir") return new(_workspace, profile, database, collection, null, operation);
        if (selected?.IdentityFilter is null) throw new InvalidOperationException("Selecione um documento com _id; não exclua esse campo da projeção.");
        // Fetch the complete document explicitly before editing a potentially projected result.
        var page = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, selected.IdentityFilter, Limit: 1));
        if (page.Documents.Count != 1) throw new InvalidOperationException("O documento não está mais disponível. Atualize a página.");
        return new(_workspace, profile, database, collection, new ResultDocumentViewModel(page.Documents[0], 0, representation), operation);
    }

    public string Title => (string.IsNullOrEmpty(FilePath) ? Mode == "Script" ? "Sem título.js" : string.IsNullOrEmpty(Collection) ? Mode : Collection : Path.GetFileName(FilePath)) + (IsDirty ? " •" : "");
    public string Context => $"{Profile?.Name ?? "Sem conexão"} › {(string.IsNullOrWhiteSpace(Database) ? "Escolha um banco" : Database)}{(IsConsole || string.IsNullOrWhiteSpace(Collection) ? "" : " › " + Collection)}";
    public string AccessHint => Profile is null ? "Escolha uma conexão para esta aba." : !IsConnected ? "Desconectado — abra a conexão para executar." : Profile.IsReadOnly ? "Somente leitura · " + Profile.RoutingLabel : "Destino fixo desta aba · " + Profile.RoutingLabel;
    public bool IsScript => Mode == "Script";
    public bool IsAggregation => Mode == "Agregação";
    public bool CanExplainAggregation => IsAggregation && CanExecute;
    public bool IsQuery => Mode == "Consulta JSON";
    public double CodeLineHeight => CodeFontSize * 1.5;
    partial void OnCodeFontSizeChanged(double value) => OnPropertyChanged(nameof(CodeLineHeight));
    public bool CanEditContext => !IsRunning;
    public bool CanExecute => !IsRunning && IsConnected && Profile is not null && !string.IsNullOrWhiteSpace(Database) && !string.IsNullOrWhiteSpace(Text)
        && (IsConsole || (IsScript ? !Profile.IsReadOnly : !string.IsNullOrWhiteSpace(Collection)));
    public bool CanExport => Documents.Count > 0 && !IsRunning;
    partial void OnProfileChanged(ConnectionProfile? value)
    {
        if (value is not null) { MissingProfileId = value.Id; MissingTargetHost = value.TargetHost; }
        _historyGeneration++;
        History.Clear(); SavedQueries.Clear(); SelectedHistory = null; SelectedSavedQuery = null;
        InvalidateDestinationResults();
    }
    partial void OnDatabaseChanged(string value) => InvalidateDestinationResults();
    partial void OnCollectionChanged(string value) => InvalidateDestinationResults();
    private void InvalidateDestinationResults()
    {
        if (IsRunning) return;
        _resultRenderable = false;
        ClearResults(NotExecutedText);
        Metrics = ""; Messages = ""; Errors = "";
        OnPropertyChanged(nameof(CanExport));
    }

    public WorkspaceTabViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Text) or nameof(InputJson) or nameof(PersistInput) or nameof(Database) or nameof(Collection) or nameof(Mode) or nameof(Profile) or nameof(FilePath) or nameof(Projection) or nameof(Sort) or nameof(Limit) or nameof(Skip) or nameof(Hint) or nameof(Comment) or nameof(Collation) or nameof(BatchSize) or nameof(MaxTimeMs) or nameof(HistoryEnabled) or nameof(ScriptHistoryEnabled))
            {
                if (!_restoring)
                {
                    IsDirty = true;
                    DraftChanged?.Invoke(this, EventArgs.Empty);
                }
                OnPropertyChanged(nameof(Context));
                OnPropertyChanged(nameof(IsScript));
                OnPropertyChanged(nameof(IsQuery));
                OnPropertyChanged(nameof(IsConsole));
                OnPropertyChanged(nameof(IsAggregation));
            }
            if (e.PropertyName is nameof(Profile) or nameof(IsConnected)) OnPropertyChanged(nameof(AccessHint));
            if (e.PropertyName is nameof(IsDirty) or nameof(FilePath) or nameof(Collection) or nameof(Mode)) OnPropertyChanged(nameof(Title));
            if (e.PropertyName is nameof(IsRunning) or nameof(Text) or nameof(Profile) or nameof(IsConnected) or nameof(Database) or nameof(Collection) or nameof(Mode))
            {
                OnPropertyChanged(nameof(CanExecute));
                OnPropertyChanged(nameof(CanEditContext));
                OnPropertyChanged(nameof(CanExport));
                ExecuteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanExplainAggregation));
                ExplainAggregationCommand.NotifyCanExecuteChanged();
            }
        };
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ExecuteAsync(string? selection)
    {
        if (!CanExecute) return;
        var profile = Profile!;
        var database = Database;
        var collection = Collection;
        var mode = Mode;
        // Snapshot every input before the first await. Selection is never replaced after an error.
        var text = string.IsNullOrEmpty(selection) ? Text : selection;
        var input = InputJson;
        var query = BuildQuery(text);
        var limit = Limit;
        var maxTimeMs = MaxTimeMs;
        var historyEnabled = HistoryEnabled && !ContainsResultData;
        var uuidPolicy = UuidPolicy;
        var executedAt = DateTimeOffset.UtcNow;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var operation = Operations.Begin($"Executando consulta em {profile.Name} › {database}", ApplicationOperationPriority.High);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _cancellation = cancellation;
        IsRunning = true;
        Errors = "";
        Messages = "";
        Metrics = "";
        _resultRenderable = false; _resultPolicy = uuidPolicy; _resultProfileId = profile.Id; _resultIsConsole = mode == "Console"; _resultEmptyText = null;
        ClearResults("Executando…");
        Status = "Executando…";
        try
        {
            if (mode == "Console")
            {
                var result = await _workspace.ExecuteConsoleAsync(new(profile, database, text, Math.Clamp(limit, 1, 1000), Math.Clamp(maxTimeMs, 1, 300000), historyEnabled), ConfirmConsoleWrite, cancellation.Token);
                _resultEmptyText = result.Results.Count == 0 ? "Nenhuma expressão retornou resultado. Consulte Mensagens." : null;
                _resultMetrics = $"{result.Results.Count} resultado(s) · {result.Duration.TotalMilliseconds:F0} ms · {result.Environment} · máximo {Math.Clamp(limit, 1, 1000)} documentos por cursor";
                await SetResultsAsync(() => result.Results.Select(StructuredResultSet.FromConsole).ToArray(), result.Results, cancellation.Token);
                Messages = result.Messages; Errors = result.Error ?? "";
                Status = result.IsCanceled ? "Cancelado" : result.IsTimedOut ? "Tempo limite excedido" : result.Error is null ? "Concluído" : "Erro";
                ResultTabIndex = result.Error is null ? 0 : 2;
            }
            else if (mode == "Script")
            {
                var result = await _workspace.ExecuteScriptAsync(profile, text, input, database, cancellation.Token);
                _resultEmptyText = result.Results.Count == 0 ? "O script não emitiu documentos. Consulte Mensagens." : null;
                _resultMetrics = $"{result.Results.Count} documento(s) · {result.Duration.TotalMilliseconds:F0} ms";
                await SetResultsAsync(() => [StructuredResultSet.FromDocuments(1, new ResultOrigin("Script mongosh", profile.Id, profile, database, null), result.Results, false, ResultCompleteness.Unknown)], null, cancellation.Token);
                Messages = result.StandardOutput;
                Errors = result.StandardError;
                Status = result.ExitCode == 0 ? "Concluído" : $"Falha — código {result.ExitCode}";
                ResultTabIndex = result.ExitCode != 0 || Errors.Length > 0 ? 2 : Documents.Count == 0 ? 1 : 0;
            }
            else
            {
                var aggregation = mode == "Agregação";
                var result = aggregation
                    ? await _workspace.AggregateAsync(profile, new AggregationQuery(database, collection, text, limit), cancellation.Token)
                    : await _workspace.QueryAsync(profile, query, cancellation.Token);
                var completeness = aggregation ? ResultCompleteness.Derived
                    : query.ProjectionJson is null || ExtendedJsonComparer.AreEquivalent(query.ProjectionJson, "{}") ? ResultCompleteness.Complete : ResultCompleteness.PartialProjection;
                _resultEmptyText = result.Documents.Count == 0 ? "Nenhum documento encontrado." : null;
                _resultMetrics = $"{result.Documents.Count} documento(s) · limite {query.Limit} · {result.Duration.TotalMilliseconds:F0} ms{(result.IsTruncated ? " · resultado limitado" : "")}";
                await SetResultsAsync(() => [StructuredResultSet.FromDocuments(1, new ResultOrigin(aggregation ? "Agregação" : "Consulta", profile.Id, profile, database, collection),
                    result.Documents, result.IsTruncated, completeness, aggregation ? "aggregate" : "find")], null, cancellation.Token);
                Status = "Concluído";
                ResultTabIndex = 0;
                if (historyEnabled && mode == "Consulta JSON")
                {
                    try
                    {
                        var entry = QueryHistoryEntry.Create(profile.Id, query);
                        await _workspace.SaveQueryHistoryAsync(entry, cancellation.Token);
                        History.Insert(0, entry);
                        while (History.Count > 50) History.RemoveAt(History.Count - 1);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { Messages = "Consulta concluída; histórico não salvo: " + OperationErrorMessages.Describe(ex); }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = "Cancelado";
            if (!_resultRenderable) SetResultState("Execução interrompida.");
            Messages = "Efeitos já enviados ao MongoDB não são revertidos automaticamente. O resultado no servidor pode ser incerto.";
            ResultTabIndex = 1;
        }
        catch (Exception ex)
        {
            Status = "Falha na execução";
            Errors = OperationErrorMessages.Describe(ex);
            if (!_resultRenderable) SetResultState("Não foi possível concluir a execução.");
            ResultTabIndex = 2;
        }
        finally
        {
            if (mode == "Agregação" && historyEnabled)
            {
                try
                {
                    var entry = new ConsoleHistoryEntry(1, Guid.NewGuid(), executedAt, profile.Id, profile.Name, database,
                        "Ambiente não registrado", text, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, Status, [profile.Id])
                    { Mode = mode, Collection = collection, DocumentLimit = limit, TargetHost = profile.TargetHost };
                    await _workspace.SaveExecutionHistoryAsync(entry, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Messages += "\nHistórico não salvo: " + OperationErrorMessages.Describe(ex);
                    if (Errors.Length == 0) ResultTabIndex = 1;
                }
            }
            operation.Complete(Status == "Cancelado" ? ApplicationOperationStatus.Cancelled : Errors.Length > 0 || Status.StartsWith("Falha", StringComparison.Ordinal) ? ApplicationOperationStatus.Error : ApplicationOperationStatus.Success,
                $"{Status} — {profile.Name} › {database}");
            _cancellation = null; IsRunning = false; ApplyPendingUuidPolicy();
        }
    }

    [RelayCommand] private void Cancel() { _cancellation?.Cancel(); _presentationCancellation?.Cancel(); }

    public async Task SaveAsync(string path)
    {
        var text = Text;
        var persistInput = PersistInput; var input = InputJson; var historyEnabled = ScriptHistoryEnabled;
        await _workspace.SaveScriptAsync(path, text);
        FilePath = path;
        _savedText = text;
        IsDirty = Text != _savedText;
        Status = "Arquivo salvo";
        DraftChanged?.Invoke(this, EventArgs.Empty);
        if (historyEnabled)
        {
            try { await _workspace.SaveScriptHistoryAsync(ScriptHistoryEntry.Create(path, inputJson: persistInput ? input : null)); }
            catch (Exception ex) { Messages = "Arquivo salvo; histórico não salvo: " + OperationErrorMessages.Describe(ex); }
        }
    }

    public async Task OpenAsync(string path)
    {
        var text = await _workspace.LoadScriptAsync(path);
        Text = text;
        FilePath = path;
        _savedText = text;
        IsDirty = false;
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    public WorkspaceDraft Snapshot() => new()
    {
        Id = Id, ContainsResultData = ContainsResultData, ProfileId = Profile?.Id ?? MissingProfileId, TargetHost = Profile?.TargetHost ?? MissingTargetHost, Database = Database, Collection = Collection,
        Mode = Mode, Text = Text, InputJson = PersistInput ? InputJson : null, FilePath = FilePath,
        IsDirty = IsDirty, Projection = Projection, Sort = Sort, Limit = Limit, Skip = Skip,
        Hint = Hint, Comment = Comment, Collation = Collation, BatchSize = BatchSize, MaxTimeMs = MaxTimeMs,
        HistoryEnabled = HistoryEnabled, ScriptHistoryEnabled = ScriptHistoryEnabled
    };

    public void Restore(WorkspaceDraft draft, ConnectionProfile? profile)
    {
        _restoring = true;
        Id = draft.Id;
        MissingProfileId = draft.ProfileId;
        MissingTargetHost = draft.TargetHost;
        Profile = profile is null ? null : profile with { TargetHost = draft.TargetHost ?? profile.TargetHost };
        Database = draft.Database;
        Collection = draft.Collection;
        Mode = draft.Mode == "Consulta JSON" ? "Console" : Modes.Contains(draft.Mode) ? draft.Mode : "Console";
        Text = draft.Mode == "Consulta JSON" ? ConsoleScripts.FromQuery(new MongoQuery(draft.Database, draft.Collection, draft.Text,
            ProjectionJson: EmptyToNull(draft.Projection), SortJson: EmptyToNull(draft.Sort), Limit: draft.Limit, Skip: draft.Skip,
            HintJson: EmptyToNull(draft.Hint), MaxTimeMs: draft.MaxTimeMs, Comment: EmptyToNull(draft.Comment), BatchSize: draft.BatchSize, CollationJson: EmptyToNull(draft.Collation))) : draft.Text;
        FilePath = draft.FilePath;
        PersistInput = draft.InputJson is not null;
        InputJson = draft.InputJson ?? "{}";
        Projection = draft.Projection;
        Sort = draft.Sort;
        Limit = Math.Clamp(draft.Limit, 1, 1000);
        Skip = Math.Max(0, draft.Skip);
        Hint = draft.Hint; Comment = draft.Comment; Collation = draft.Collation;
        BatchSize = Math.Clamp(draft.BatchSize, 1, 10000); MaxTimeMs = Math.Clamp(draft.MaxTimeMs, 1, 600000);
        HistoryEnabled = draft.HistoryEnabled; ScriptHistoryEnabled = draft.ScriptHistoryEnabled;
        IsDirty = draft.IsDirty || draft.Mode == "Consulta JSON";
        IsConnected = false;
        _savedText = IsDirty ? "" : Text;
        _restoring = false;
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        var generation = ++_historyGeneration;
        var profile = Profile;
        try
        {
            History.Clear(); SavedQueries.Clear(); ScriptHistory.Clear(); ConsoleHistory.Clear();
            var consoleEntries = await _workspace.GetConsoleHistoryAsync();
            if (generation != _historyGeneration) return;
            foreach (var entry in consoleEntries) ConsoleHistory.Add(entry);
            if (profile is not null)
            {
                var history = HistoryEnabled ? await _workspace.GetRecentQueryHistoryAsync(profile.Id) : [];
                var saved = await _workspace.GetSavedQueriesAsync(profile.Id);
                if (generation != _historyGeneration) return;
                foreach (var entry in history) History.Add(entry);
                foreach (var entry in saved) SavedQueries.Add(entry);
            }
            var scripts = ScriptHistoryEnabled ? await _workspace.GetRecentScriptHistoryAsync() : [];
            if (generation != _historyGeneration) return;
            foreach (var entry in scripts) ScriptHistory.Add(entry);
        }
        catch (Exception ex) { Errors = OperationErrorMessages.Describe(ex); ResultTabIndex = 2; }
    }

    [RelayCommand]
    private void ApplyHistory()
    {
        if (IsRunning) return;
        var query = SelectedHistory?.ToQuery() ?? SelectedSavedQuery?.ToQuery();
        if (query is null) return;
        Mode = "Console"; Database = query.Database; Collection = query.Collection;
        Text = ConsoleScripts.FromQuery(query); Projection = query.ProjectionJson ?? ""; Sort = query.SortJson ?? "";
        Limit = query.Limit; Skip = query.Skip;
        Hint = query.HintJson ?? ""; Comment = query.Comment ?? ""; Collation = query.CollationJson ?? "";
        BatchSize = query.BatchSize ?? 100; MaxTimeMs = query.MaxTimeMs ?? 30000;
    }

    [RelayCommand]
    private async Task SaveQueryAsync()
    {
        if (Profile is null || !IsQuery) return;
        try
        {
            var query = BuildQuery(Text);
            var saved = SavedQuery.Create(SavedQueryName, Profile.Id, query, SavedQueryIsFavorite);
            if (SelectedSavedQuery is { } existing) saved = saved with { Id = existing.Id };
            await _workspace.SaveSavedQueryAsync(saved);
            await LoadHistoryAsync();
        }
        catch (Exception ex) { Errors = OperationErrorMessages.Describe(ex); ResultTabIndex = 2; }
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private MongoQuery BuildQuery(string text) => new(Database, Collection, text, ProjectionJson: EmptyToNull(Projection), SortJson: EmptyToNull(Sort),
        Limit: Limit, Skip: Skip, HintJson: EmptyToNull(Hint), MaxTimeMs: MaxTimeMs, Comment: EmptyToNull(Comment), BatchSize: BatchSize, CollationJson: EmptyToNull(Collation));

    [RelayCommand] private async Task NextPageAsync() { if (!CanExecute || !IsQuery) return; Skip = Math.Min(1000000, Skip + Limit); await ExecuteCommand.ExecuteAsync(null); }
    [RelayCommand] private async Task PreviousPageAsync() { if (!CanExecute || !IsQuery) return; Skip = Math.Max(0, Skip - Limit); await ExecuteCommand.ExecuteAsync(null); }
    [RelayCommand] private async Task FirstDocumentsAsync() { if (IsRunning || !IsQuery) return; Text = "{}"; Skip = 0; if (CanExecute) await ExecuteCommand.ExecuteAsync(null); }
    partial void OnSelectedSavedQueryChanged(SavedQuery? value)
    {
        if (value is null) return;
        SavedQueryName = value.Name; SavedQueryIsFavorite = value.IsFavorite;
    }
    [RelayCommand] private void NewSavedQuery() { SelectedSavedQuery = null; SavedQueryName = ""; SavedQueryIsFavorite = false; }
    [RelayCommand] private async Task DeleteSavedQueryAsync()
    {
        if (SelectedSavedQuery is null) return;
        try { await _workspace.DeleteSavedQueryAsync(SelectedSavedQuery.Id); NewSavedQuery(); await LoadHistoryAsync(); }
        catch (Exception ex) { Errors = OperationErrorMessages.Describe(ex); }
    }
}
