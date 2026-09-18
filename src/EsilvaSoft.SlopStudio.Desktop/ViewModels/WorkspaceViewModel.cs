using EsilvaSoft.SlopStudio.LocalAi.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceService _workspace;
    private readonly IWorkspaceSessionRepository _sessions;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _debounce;
    private bool _initialized;
    private bool _disposed;
    private bool _updatingRecovery;
    private AutocompleteSettings _autocompleteSettings = new();
    // Persisted form kept as loaded (null = absent) so autosave never drops or materializes custom shortcuts.
    private EditorKeyBindings? _keyBindings;
    private readonly HashSet<Guid> _excludedProfiles = [];
    private readonly Dictionary<Guid, UuidRepresentation> _profileUuidRepresentations = [];
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly bool _ownsMetadata;
    public WorkspaceService Workspace => _workspace;
    /// <summary>Autocomplete metadata; explorer loads write into it and connected roots allow refreshes.</summary>
    public IMetadataCache Metadata { get; }
    public ApplicationStatusViewModel Operations { get; }
    public AppUpdateViewModel Updates { get; }
    public UuidPreferenceViewModel UuidPreferences { get; }
    public IdentifierPreferenceViewModel IdentifierPreferences { get; }
    public IAutocompleteService AutocompleteService { get; }
    /// <summary>Deterministic catalog completion shared by all editor tabs.</summary>
    public ICompletionProvider? TraditionalCompletion { get; }
    /// <summary>
    /// Sinal de uso da sessão, compartilhado por todas as abas: quem aprende é o usuário, não a aba, e uma sugestão
    /// aceita em uma aba deve subir na seguinte. É só memória — nomes, sem valores — e nunca chega ao LiteDB nem à
    /// sessão persistida. Compartilhar isto não é compartilhar cancelamento: o CancellationTokenSource segue por aba.
    /// </summary>
    public CompletionUsageTracker CompletionUsage { get; } = new();
    public IAiChatService AiChatService { get; }
    public AutocompleteSettingsViewModel AutocompletePreferences { get; }
    /// <summary>Effective editor shortcuts per command id; defaults until a readable session is loaded.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> KeyBindings { get; private set; } = EditorKeyBindings.Resolve(null);
    public ExplorerDetailsViewModel Details { get; }
    public IReadOnlyList<string> Themes { get; } = ["Sistema", "Claro", "Escuro"];
    [ObservableProperty] private string _theme = "Sistema";
    [ObservableProperty] private double _codeFontSize = 14;
    [ObservableProperty] private double _explorerWidth = 260;
    [ObservableProperty] private double _editorRatio = .6;
    [ObservableProperty] private bool _recoverDrafts = true;
    [ObservableProperty] private bool _recoverActiveProfile = true;
    [ObservableProperty] private string _sessionStatus = "";
    public event EventHandler? ThemeChanged;
    public event EventHandler? LayoutChanged;

    public WorkspaceViewModel(WorkspaceService workspace, IWorkspaceSessionRepository sessions, IAutocompleteService? autocomplete = null, ILocalModelCatalog? modelCatalog = null, IAiChatService? aiChat = null,
        IKnowledgeCatalog? knowledgeCatalog = null,
        ILocalAiModelService? localModels = null, IAppUpdateService? updates = null, IRemoteModelSource? remoteModels = null, IMetadataCache? metadata = null)
    {
        _workspace = workspace; _sessions = sessions; Operations = new(workspace.Operations); Details = new ExplorerDetailsViewModel(workspace);
        // Without a registered driver source, explorer write-through still feeds highlighting and names; remote loads stay unavailable.
        _ownsMetadata = metadata is null;
        Metadata = metadata ?? new MetadataCache(UnavailableMetadataSource.Instance);
        Metadata.Changed += OnMetadataChanged;
        Updates = new(updates, workspace.Operations);
        AutocompleteService = autocomplete ?? new AutocompleteService();
        var effectiveCatalog = knowledgeCatalog ?? new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(Metadata)]);
        TraditionalCompletion = new TraditionalCompletionProvider(
            new CompletionService(effectiveCatalog, new CompletionRanker(usage: CompletionUsage),
                new WorkspaceCompletionProfileResolver(() => Profiles)));
        AiChatService = aiChat ?? new AiChatService();
        AutocompletePreferences = new(AutocompleteService, modelCatalog, async settings =>
        {
            var previous = _autocompleteSettings;
            _autocompleteSettings = settings;
            try { await SaveSessionAsync(); }
            catch { _autocompleteSettings = previous; throw; }
            await AutocompleteService.ConfigureAsync(settings);
        }, localModels, remoteModels, workspace.Operations);
        Roots.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoConnections));
        UuidPreferences = new("Representação UUID · Binary BSON", allowInherit: false, () => UuidRepresentation, value => SetUuidRepresentationAsync(value ?? UuidRepresentation.Standard));
        IdentifierPreferences = new(() => UuidRepresentation, SetIdentifierModeAsync);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await ReloadProfilesAsync();
            var session = await _sessions.LoadSessionAsync();
            _autocompleteSettings = session.Preferences.Autocomplete.Validate();
            // Invalid shortcuts fail here too, before _initialized, so no save path can replace the snapshot.
            KeyBindings = EditorKeyBindings.Resolve(session.Preferences.EditorKeyBindings);
            _keyBindings = session.Preferences.EditorKeyBindings;
            // Startup only reads preferences; the model is validated and loaded on the first AI request.
            await AutocompleteService.ConfigureAsync(_autocompleteSettings);
            AutocompletePreferences.Load(_autocompleteSettings);
            Theme = Themes.Contains(session.Preferences.Theme) ? session.Preferences.Theme : "Sistema";
            CodeFontSize = Math.Clamp(session.Preferences.CodeFontSize, 12, 20);
            ExplorerWidth = Math.Clamp(session.Preferences.ExplorerWidth, 200, 420);
            EditorRatio = Math.Clamp(session.Preferences.EditorRatio, .25, .75);
            RecoverDrafts = session.Preferences.RecoverDrafts;
            _excludedProfiles.UnionWith(session.Preferences.ExcludedProfileIds);
            foreach (var profileId in session.Preferences.SchemaSamplingProfileIds) Metadata.SetSchemaSamplingAllowed(profileId, true);
            foreach (var entry in session.Preferences.ProfileUuidRepresentations) _profileUuidRepresentations[entry.Key] = entry.Value;
            UuidRepresentation = session.Preferences.UuidRepresentation;
            IdentifierMode = session.Preferences.IdentifierMode;
            UuidPreferences.Load(UuidRepresentation);
            IdentifierPreferences.Load(IdentifierMode);
            if (RecoverDrafts)
                foreach (var draft in session.Tabs.Where(d => !d.ContainsResultData && (d.ProfileId is null || !_excludedProfiles.Contains(d.ProfileId.Value))))
                {
                    var tab = CreateTab();
                    tab.Restore(draft, Profiles.FirstOrDefault(p => p.Id == draft.ProfileId));
                    Register(tab);
                }
            ActiveTab = Tabs.FirstOrDefault(t => t.Id == session.ActiveTabId) ?? Tabs.FirstOrDefault();
            _initialized = true;
            if (Tabs.Count == 0) NewTab();
            SessionStatus = session.Tabs.Length > 0 ? "Rascunhos recuperados; conexões permanecem fechadas." : "";
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Keep the unreadable snapshot intact; never silently overwrite it with an empty session.
            SessionStatus = "Não foi possível recuperar a sessão: " + ex.Message;
            if (Tabs.Count == 0) NewTab();
        }
    }

    partial void OnThemeChanged(string value) { ThemeChanged?.Invoke(this, EventArgs.Empty); ScheduleSave(); }
    partial void OnCodeFontSizeChanged(double value) { foreach (var tab in Tabs) tab.CodeFontSize = value; ScheduleSave(); }
    partial void OnExplorerWidthChanged(double value) => ScheduleSave();
    partial void OnEditorRatioChanged(double value) => ScheduleSave();
    partial void OnRecoverDraftsChanged(bool value) => ScheduleSave();

    private void ScheduleSave()
    {
        if (!_initialized || _disposed) return;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_debounce.Token);
    }
    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(750, cancellationToken); await SaveSessionAsync(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { SessionStatus = "Rascunho não salvo: " + ex.Message; }
    }

    public async Task SaveSessionAsync()
    {
        if (!_initialized) throw new InvalidOperationException("A sessão anterior não foi carregada. Ela será preservada; salve seus scripts em arquivos.");
        await _saveGate.WaitAsync();
        try
        {
            var session = new WorkspaceSession
            {
                ActiveTabId = ActiveTab?.Id,
                Preferences = new WorkspacePreferences { Autocomplete = _autocompleteSettings, Theme = Theme, CodeFontSize = CodeFontSize, ExplorerWidth = ExplorerWidth, EditorRatio = EditorRatio, RecoverDrafts = RecoverDrafts, ExcludedProfileIds = _excludedProfiles.ToArray(),
                    UuidRepresentation = UuidRepresentation, ProfileUuidRepresentations = new(_profileUuidRepresentations), IdentifierMode = IdentifierMode,
                    SchemaSamplingProfileIds = Metadata.SchemaSamplingProfiles.ToArray(), EditorKeyBindings = _keyBindings },
                Tabs = Tabs.Select(t => t.Snapshot()).ToArray()
            };
            await _sessions.SaveSessionAsync(session);
            SessionStatus = RecoverDrafts ? "Rascunhos locais atualizados" : "Recuperação de rascunhos desativada";
        }
        catch (Exception ex) { SessionStatus = "Rascunho não salvo: " + ex.Message; throw; }
        finally { _saveGate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Operations.Dispose();
        Updates.Dispose();
        _debounce?.Cancel(); _debounce?.Dispose();
        foreach (var root in Roots) root.Invalidate();
        Details.Clear();
        foreach (var tab in Tabs) { tab.DraftChanged -= OnDraftChanged; tab.CancelCommand.Execute(null); tab.Dispose(); }
        Metadata.Changed -= OnMetadataChanged;
        if (_ownsMetadata && Metadata is IDisposable metadata) metadata.Dispose();
        _saveGate.Dispose();
    }
}
