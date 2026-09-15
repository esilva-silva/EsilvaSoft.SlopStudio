using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Language;
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
    public IAiChatService AiChatService { get; }
    public AutocompleteSettingsViewModel AutocompletePreferences { get; }
    public ExplorerDetailsViewModel Details { get; }
    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];
    public ObservableCollection<ExplorerNodeViewModel> Roots { get; } = [];
    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];
    public IReadOnlyList<string> Themes { get; } = ["Sistema", "Claro", "Escuro"];
    [ObservableProperty] private WorkspaceTabViewModel? _activeTab;
    [ObservableProperty] private ExplorerNodeViewModel? _selectedNode;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _theme = "Sistema";
    [ObservableProperty] private double _codeFontSize = 14;
    [ObservableProperty] private double _explorerWidth = 260;
    [ObservableProperty] private double _editorRatio = .6;
    [ObservableProperty] private bool _recoverDrafts = true;
    [ObservableProperty] private bool _recoverActiveProfile = true;
    [ObservableProperty] private UuidRepresentation _uuidRepresentation = UuidRepresentation.Standard;
    [ObservableProperty] private IdentifierRepresentationMode _identifierMode = IdentifierRepresentationMode.Standard;
    [ObservableProperty] private string _sessionStatus = "";
    [ObservableProperty] private string _explorerStatus = "Abra uma conexão para explorar os bancos.";
    public bool HasActiveTab => ActiveTab is not null;
    public bool CanSetProfileRecovery => ActiveTab?.Profile is not null;
    public bool HasNoConnections => Roots.Count == 0;
    public bool SnapshotAllowsRecovery(Guid profileId) => !_excludedProfiles.Contains(profileId);
    public event EventHandler? ThemeChanged;
    public event EventHandler? LayoutChanged;

    public WorkspaceViewModel(WorkspaceService workspace, IWorkspaceSessionRepository sessions, IAutocompleteService? autocomplete = null, ILocalModelCatalog? modelCatalog = null, IAiChatService? aiChat = null,
        ILocalAiModelService? localModels = null, IAppUpdateService? updates = null, IRemoteModelSource? remoteModels = null, IMetadataCache? metadata = null)
    {
        _workspace = workspace; _sessions = sessions; Operations = new(workspace.Operations); Details = new ExplorerDetailsViewModel(workspace);
        // Without a registered driver source, explorer write-through still feeds highlighting and names; remote loads stay unavailable.
        _ownsMetadata = metadata is null;
        Metadata = metadata ?? new MetadataCache(UnavailableMetadataSource.Instance);
        Metadata.Changed += OnMetadataChanged;
        Updates = new(updates, workspace.Operations);
        AutocompleteService = autocomplete ?? new AutocompleteService();
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

    public UuidDisplayPolicy CaptureUuidPolicy() => new(UuidRepresentation, _profileUuidRepresentations, IdentifierMode);

    /// <summary>Applies the global identifier mode to idle tabs and persists it. The UUID representation is not touched.</summary>
    public async Task SetIdentifierModeAsync(IdentifierRepresentationMode value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), "Modo de identificador desconhecido.");
        IdentifierMode = value;
        await SaveSessionAsync();
    }

    partial void OnIdentifierModeChanged(IdentifierRepresentationMode value)
    {
        UuidPreferences.IsPreviewVisible = value != IdentifierRepresentationMode.ObjectId;
        PublishUuidPolicy();
    }
    public UuidRepresentation? GetProfileUuidRepresentation(Guid? profileId) =>
        profileId is { } id && _profileUuidRepresentations.TryGetValue(id, out var value) ? value : null;

    /// <summary>Applies the global preference to idle tabs immediately and persists it; a failed save stays visible and in memory.</summary>
    public async Task SetUuidRepresentationAsync(UuidRepresentation value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), "Representação UUID desconhecida.");
        UuidRepresentation = value;
        await SaveSessionAsync();
    }

    public async Task SetProfileUuidRepresentationAsync(Guid profileId, UuidRepresentation? value)
    {
        if (value is { } defined && !Enum.IsDefined(defined)) throw new ArgumentOutOfRangeException(nameof(value), "Representação UUID desconhecida.");
        if (GetProfileUuidRepresentation(profileId) == value) return;
        if (value is null) _profileUuidRepresentations.Remove(profileId); else _profileUuidRepresentations[profileId] = value.Value;
        PublishUuidPolicy();
        await SaveSessionAsync();
    }

    private void PublishUuidPolicy()
    {
        var policy = CaptureUuidPolicy();
        foreach (var tab in Tabs) tab.UuidPolicy = policy;
    }

    partial void OnUuidRepresentationChanged(UuidRepresentation value)
    {
        PublishUuidPolicy();
        IdentifierPreferences?.RefreshPreview();
    }

    public async Task InitializeAsync()
    {
        try
        {
            await ReloadProfilesAsync();
            var session = await _sessions.LoadSessionAsync();
            _autocompleteSettings = session.Preferences.Autocomplete.Validate();
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

    public async Task ReloadProfilesAsync()
    {
        var profiles = await _workspace.GetProfilesAsync();
        Profiles.Clear(); foreach (var profile in profiles) Profiles.Add(profile);
        foreach (var root in Roots.ToArray())
        {
            if (profiles.FirstOrDefault(p => p.Id == root.Profile.Id) != (root.Profile with { TargetHost = null }))
            {
                root.Invalidate(); Roots.Remove(root);
                if (SelectedNode?.Profile.Id == root.Profile.Id) SelectedNode = null;
                ExplorerStatus = "Perfil alterado ou removido — abra novamente a conexão para atualizar o destino.";
            }
        }
        foreach (var profile in profiles)
            if (!Roots.Any(r => r.Profile.Id == profile.Id)) Roots.Add(CreateRoot(profile));
        foreach (var tab in Tabs)
        {
            var id = tab.Profile?.Id ?? tab.MissingProfileId;
            var profile = Profiles.FirstOrDefault(p => p.Id == id);
            // Re-evaluate access restrictions for future operations; an in-flight operation keeps its snapshot.
            tab.Profile = profile is null ? null : profile with { TargetHost = tab.Profile?.TargetHost ?? tab.MissingTargetHost };
            tab.IsConnected = profile is not null && IsProfileConnected(tab.Profile!);
        }
    }

    private int _environmentRevision;
    public void InvalidateEnvironment()
    {
        _environmentRevision++;
        foreach (var root in Roots) root.Invalidate(); SelectedNode = null;
        foreach (var tab in Tabs) tab.IsConnected = false;
        ExplorerStatus = "Ambiente atualizado. Reabra as conexões para conferir o destino.";
    }

    private ExplorerNodeViewModel CreateRoot(ConnectionProfile profile)
    {
        var root = new ExplorerNodeViewModel(_workspace, profile, profile.Name, metadata: Metadata);
        root.ConnectionChanged += (_, _) =>
        {
            // Only connected roots may refresh metadata remotely; disconnecting drops everything cached for the profile.
            if (root.IsConnected) Metadata.Connect(root.Profile); else Metadata.Disconnect(root.Profile.Id);
            foreach (var tab in Tabs.Where(t => t.Profile?.Id == profile.Id)) tab.IsConnected = IsProfileConnected(tab.Profile!);
        };
        return root;
    }

    private void OnMetadataChanged(object? sender, MetadataChangedEventArgs e)
    {
        if (_disposed) return;
        if (_context is null || SynchronizationContext.Current == _context) RefreshSyntaxContexts();
        else _context.Post(_ => RefreshSyntaxContexts(), null);
    }

    private void RefreshSyntaxContexts()
    {
        if (_disposed) return;
        foreach (var tab in Tabs) tab.RefreshSyntaxContext();
    }

    /// <summary>Per-connection opt-in for automatic schema sampling (names and types only), persisted additively.</summary>
    public async Task SetSchemaSamplingAllowedAsync(Guid profileId, bool allowed)
    {
        Metadata.SetSchemaSamplingAllowed(profileId, allowed);
        await SaveSessionAsync();
    }

    public bool IsProfileConnected(ConnectionProfile profile) => Roots.Any(r => r.Profile.Id == profile.Id && r.Profile.TargetHost == profile.TargetHost && r.IsConnected);

    public async Task OpenConnectionAsync(ConnectionProfile profile)
    {
        var revision = _environmentRevision;
        var root = Roots.FirstOrDefault(r => r.Profile.Id == profile.Id);
        if (root is null) { root = CreateRoot(profile); Roots.Add(root); }
        await root.LoadAsync();
        if (revision != _environmentRevision || !Roots.Contains(root)) throw new InvalidOperationException("O destino mudou durante a abertura. Abra novamente a conexão.");
        if (!root.IsConnected) throw new InvalidOperationException(root.Message.Length > 0 ? root.Message : "Conexão não aberta.");
        root.IsExpanded = true;
        SelectedNode = root;
        ExplorerStatus = root.Children.Count + " banco(s) · coleções carregadas ao expandir";
        if (ActiveTab is { Profile: null, IsDirty: false }) BindActiveTab(root.Profile, root.Profile.DefaultDatabase ?? "", "");
        ApplySearch();
    }

    public void Disconnect(ExplorerNodeViewModel node)
    {
        node.Root.Invalidate();
        foreach (var tab in Tabs.Where(t => t.Profile?.Id == node.Profile.Id)) tab.IsConnected = false;
        Details.Clear();
        ExplorerStatus = "Conexão desconectada. Operações em andamento conservam seu contexto e cancelamento próprios.";
    }

    public async Task SelectInstanceAsync(ExplorerNodeViewModel node, string? host)
    {
        var root = Roots.FirstOrDefault(r => r.Profile.Id == node.Profile.Id);
        if (root is null) throw new InvalidOperationException("Perfil removido. Abra novamente as conexões.");
        var profile = root.Profile with { TargetHost = host };
        var index = Roots.IndexOf(root);
        if (index < 0) return;
        Disconnect(root);
        Roots[index] = CreateRoot(profile);
        await OpenConnectionAsync(profile);
    }

    public async Task DropExplorerIndexAsync(ExplorerNodeViewModel node)
    {
        if (!node.Root.IsConnected || node.Index is null) throw new InvalidOperationException("Selecione um índice de uma conexão aberta.");
        var profile = node.Profile;
        profile.EnsureWriteAllowed();
        var request = new IndexDropRequest(node.Database, node.Collection!, node.Index.Name).Validate();
        await _workspace.DropIndexAsync(profile, request);
        if (node.Parent is { } indexes) await indexes.LoadAsync();
        ExplorerStatus = "Índice removido: " + request.Name;
    }

    public WorkspaceTabViewModel OpenScript(ExplorerNodeViewModel node, ExplorerScriptOperation operation)
    {
        var database = string.IsNullOrWhiteSpace(node.Database) ? node.Profile.DefaultDatabase ?? "admin" : node.Database;
        var tab = CreateTab();
        tab.Restore(new WorkspaceDraft { ProfileId = node.Profile.Id, Database = database, Collection = node.Collection ?? "", Mode = "Console", HistoryEnabled = true,
            Text = ExplorerScripts.Create(operation, database, node.Collection, node.Index, CaptureUuidPolicy().ResolveOptions(node.Profile.Id)), IsDirty = true }, node.Profile);
        tab.IsConnected = IsProfileConnected(node.Profile);
        Register(tab); ActiveTab = tab; ScheduleSave();
        return tab;
    }

    public void OpenConsoleHistory(ConsoleHistoryEntry entry)
    {
        var root = Roots.FirstOrDefault(r => r.Profile.Id == entry.ProfileId);
        var tab = CreateTab();
        tab.Restore(new WorkspaceDraft { ProfileId = entry.ProfileId, TargetHost = entry.TargetHost, Database = entry.Database, Collection = entry.Collection, Mode = entry.Mode, Limit = entry.DocumentLimit, Text = entry.Script, HistoryEnabled = true }, root?.Profile is { } profile ? profile with { TargetHost = entry.TargetHost } : null);
        tab.IsConnected = tab.Profile is not null && IsProfileConnected(tab.Profile);
        tab.Messages = $"Histórico de {entry.Environment}; uma nova execução usará o ambiente ativo atual.";
        Register(tab); ActiveTab = tab; ScheduleSave();
    }

    public void OpenDocumentInEditor(WorkspaceTabViewModel source, string json)
    {
        var result = source.IsConsole ? source.SelectedConsoleResult : null;
        var profile = result?.SourceProfile ?? source.Profile;
        if (profile is null) return;
        var database = result?.Database ?? source.Database;
        var collection = result?.Collection ?? source.Collection;
        var node = new ExplorerNodeViewModel(_workspace, profile, collection, database, collection);
        var tab = OpenScript(node, ExplorerScriptOperation.Shell);
        tab.ContainsResultData = true;
        tab.Messages = "Documento no editor; recuperação automática desativada para esta aba. Use Salvar para gravar um arquivo explicitamente.";
        tab.Text = "// Documento da página carregada; nada foi executado.\nconst document = EJSON.parse(" + System.Text.Json.JsonSerializer.Serialize(json) + ");\ndocument;\n";
    }

    partial void OnSelectedNodeChanged(ExplorerNodeViewModel? value) => _ = Details.SelectAsync(value);

    public void BindActiveTab(ConnectionProfile profile, string database, string collection)
    {
        if (ActiveTab is null || ActiveTab.IsRunning) return;
        ActiveTab.Profile = profile; ActiveTab.Database = database; ActiveTab.Collection = collection;
        ActiveTab.IsConnected = IsProfileConnected(profile);
        UpdateProfileRecovery();
    }

    [RelayCommand]
    private void NewTab()
    {
        var node = SelectedNode;
        var tab = CreateTab();
        tab.Restore(new WorkspaceDraft { ProfileId = node?.Profile.Id, Database = node?.Database ?? "", Text = "// Console JavaScript: db representa o banco selecionado.\n", Mode = "Console", HistoryEnabled = true }, node?.Profile);
        tab.IsConnected = node is not null && IsProfileConnected(node.Profile);
        Register(tab); ActiveTab = tab; ScheduleSave();
    }

    public void OpenCollection(ExplorerNodeViewModel node)
    {
        if (!node.IsCollection) return;
        var existing = Tabs.FirstOrDefault(t => t.Profile?.Id == node.Profile.Id && t.Profile.TargetHost == node.Profile.TargetHost && t.Database == node.Database && t.Collection == node.Collection && t.IsConsole);
        if (existing is not null) { ActiveTab = existing; return; }
        var tab = CreateTab();
        tab.Restore(new WorkspaceDraft { ProfileId = node.Profile.Id, Database = node.Database, Collection = node.Collection!, Mode = "Console", HistoryEnabled = true, Text = ConsoleScripts.Find(node.Collection!) }, node.Profile);
        tab.IsConnected = IsProfileConnected(node.Profile);
        Register(tab); ActiveTab = tab; ScheduleSave();
    }

    public void RemoveTab(WorkspaceTabViewModel tab)
    {
        if (tab.IsRunning) throw new InvalidOperationException("Aguarde o encerramento da operação antes de fechar a aba.");
        tab.DraftChanged -= OnDraftChanged;
        Tabs.Remove(tab);
        if (ActiveTab == tab) ActiveTab = Tabs.LastOrDefault();
        ScheduleSave();
    }

    private void Register(WorkspaceTabViewModel tab)
    {
        tab.KnownSyntaxNamespaces = KnownSyntaxNamespaces;
        tab.Autocomplete = AutocompleteService;
        tab.AiChat = AiChatService;
        tab.KnownAutocompleteNames = () => KnownAutocompleteNames(tab);
        tab.UuidPolicy = CaptureUuidPolicy(); Tabs.Add(tab); tab.DraftChanged += OnDraftChanged;
    }

    private WorkspaceTabViewModel CreateTab() => new(_workspace) { CodeFontSize = CodeFontSize, AiChat = AiChatService };

    // Loaded metadata only (Peek): typing never schedules a remote refresh in this path.
    private EsilvaSoft.SlopStudio.Application.SyntaxHighlighting.SyntaxNamespace[] KnownSyntaxNamespaces()
    {
        var names = new Dictionary<Guid, string>();
        foreach (var profile in Profiles) names.TryAdd(profile.Id, profile.Name);
        var result = Profiles.Select(profile => new EsilvaSoft.SlopStudio.Application.SyntaxHighlighting.SyntaxNamespace(profile.Name)).ToList();
        foreach (var item in Metadata.SnapshotNamespaces(4096))
            if (names.TryGetValue(item.ProfileId, out var name)) result.Add(new(name, item.Database, item.Collection, item.Index));
        return result.Take(4096).ToArray();
    }

    private string[] KnownAutocompleteNames(WorkspaceTabViewModel tab)
    {
        var names = Profiles.Select(profile => profile.Name).ToList();
        foreach (var root in Roots.Where(root => root.Profile.Id == tab.Profile?.Id))
        {
            var identity = ConnectionIdentity.From(root.Profile);
            foreach (var database in Metadata.GetDatabases(identity, MetadataAccess.Peek).Value ?? [])
            {
                names.Add(database);
                if (database == tab.Database)
                    names.AddRange((Metadata.GetCollections(identity, database, MetadataAccess.Peek).Value ?? []).Select(collection => collection.Name));
            }
        }
        return names.Distinct(StringComparer.Ordinal).Take(256).ToArray();
    }
    private void OnDraftChanged(object? sender, EventArgs e) => ScheduleSave();
    partial void OnSearchChanged(string value) => ApplySearch();
    private void ApplySearch()
    {
        foreach (var root in Roots) root.Filter(Search.Trim());
        if (!string.IsNullOrWhiteSpace(Search)) ExplorerStatus = Roots.Any(r => r.IsVisible) ? "Busca limitada aos itens já carregados." : "Nenhum item carregado corresponde à busca.";
        else if (Roots.Count > 0) ExplorerStatus = "Expanda um banco para carregar coleções.";
    }
    partial void OnThemeChanged(string value) { ThemeChanged?.Invoke(this, EventArgs.Empty); ScheduleSave(); }
    partial void OnCodeFontSizeChanged(double value) { foreach (var tab in Tabs) tab.CodeFontSize = value; ScheduleSave(); }
    partial void OnExplorerWidthChanged(double value) => ScheduleSave();
    partial void OnEditorRatioChanged(double value) => ScheduleSave();
    partial void OnRecoverDraftsChanged(bool value) => ScheduleSave();
    partial void OnActiveTabChanged(WorkspaceTabViewModel? value)
    {
        OnPropertyChanged(nameof(HasActiveTab)); OnPropertyChanged(nameof(CanSetProfileRecovery));
        UpdateProfileRecovery(); ScheduleSave();
    }
    private void UpdateProfileRecovery()
    {
        var enabled = ActiveTab?.Profile is not { } profile || !_excludedProfiles.Contains(profile.Id);
        _updatingRecovery = true;
        RecoverActiveProfile = enabled;
        _updatingRecovery = false;
        OnPropertyChanged(nameof(CanSetProfileRecovery));
    }
    partial void OnRecoverActiveProfileChanged(bool value)
    {
        if (_updatingRecovery || ActiveTab?.Profile is not { } profile) return;
        if (value) _excludedProfiles.Remove(profile.Id); else _excludedProfiles.Add(profile.Id);
        ScheduleSave();
    }

    [RelayCommand]
    private async Task RefreshExplorerAsync()
    {
        try
        {
            if (SelectedNode is { } node)
            {
                await (node.CanExpand ? node : node.Parent ?? node).LoadAsync();
                await Details.SelectAsync(node);
            }
            else await ReloadProfilesAsync();
            ApplySearch();
        }
        catch (Exception ex) { ExplorerStatus = ex.Message; }
    }

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
                    SchemaSamplingProfileIds = Metadata.SchemaSamplingProfiles.ToArray() },
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
        foreach (var tab in Tabs) { tab.DraftChanged -= OnDraftChanged; tab.CancelCommand.Execute(null); }
        Metadata.Changed -= OnMetadataChanged;
        if (_ownsMetadata && Metadata is IDisposable metadata) metadata.Dispose();
        _saveGate.Dispose();
    }
}
