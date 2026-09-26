using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public enum AgentChatState
{
    Unavailable,
    NoProvider,
    ProviderUnavailable,
    NotAuthenticated,
    CredentialExpired,
    VaultUnavailable,
    Ready,
    Reviewing,
    Connecting,
    Generating,
    WaitingTool,
    WaitingApproval,
    Cancelling,
    Completed,
    Cancelled,
    OutcomeUnknown,
    TimedOut,
    Failed,
    ContextFailed,

    /// <summary>A CLI-delegated provider has no usable working directory: sending is disabled (no fallback).</summary>
    ReadScopeUnavailable,
}

/// <summary>Immutable package reviewed by the user. Sending uses exactly this, never a re-read of mutable state.</summary>
public sealed record AgentChatPreview(
    string ProviderId,
    string? ModelId,
    AgentContextScope Scope,
    string Message,
    AgentChatTabSnapshot Tab,
    AgentContextSnapshot Snapshot,
    string DestinationText,
    string ScopeText,
    string NamespaceText,
    string ContextText);

public sealed record AgentContextScopeOption(AgentContextScope Scope, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Chat of one workspace tab. The context comes only from the tab snapshot delegate supplied by the host (the tab's own
/// explicit destination), is captured synchronously before any await, and never follows the explorer selection.
/// Provider, context scope and external destination are explicit user choices; configuring an account does not
/// consent to sending data. Conversations are ephemeral: nothing here is persisted.
/// </summary>
public sealed partial class AgentChatViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AgentChatServices _services;
    private readonly Func<AgentChatTabSnapshot> _captureTab;
    private readonly Func<string?>? _captureWorkspaceFolder;
    private readonly CancellationTokenSource _lifetime = new();
    private long _reviewGeneration;
    private bool _suppressInvalidation;
    private IReadOnlyList<AgentProviderPresentation>? _lastListing;

    /// <param name="services">Chat services; missing pieces make the feature unavailable.</param>
    /// <param name="captureTab">Synchronous capture of the originating tab.</param>
    /// <param name="captureWorkspaceFolder">
    /// Synchronous read of the Files panel folder (UI thread). Captured when a session starts and sent as
    /// <see cref="AgentSessionOptions.WorkingDirectory"/>; null means no folder.
    /// </param>
    public AgentChatViewModel(AgentChatServices services, Func<AgentChatTabSnapshot> captureTab, Func<string?>? captureWorkspaceFolder = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(captureTab);
        _services = services;
        _captureTab = captureTab;
        _captureWorkspaceFolder = captureWorkspaceFolder;
        ContextScopes =
        [
            new(AgentContextScope.None, Text.Resolve("agentScopeNone")),
            new(AgentContextScope.Metadata, Text.Resolve("agentScopeMetadata")),
            new(AgentContextScope.Selection, Text.Resolve("agentScopeSelection")),
        ];
        _selectedScope = ContextScopes[0];
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowEmptyInvitation));
        };
        LocalizationViewModel.Current.PropertyChanged += OnLocalizationChanged;
        RefreshTabContext();
        LoadProviders(null);
    }

    public event EventHandler<AgentApprovalViewModel>? ApprovalRequested;

    public event EventHandler? SettingsRequested;

    /// <summary>Asks the view to move focus back to the composer after a user action; streaming never raises it.</summary>
    public event EventHandler? ComposerFocusRequested;

    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public ObservableCollection<AgentProviderOption> Providers { get; } = [];

    public ObservableCollection<string> Models { get; } = [];

    public IReadOnlyList<AgentContextScopeOption> ContextScopes { get; }

    public ObservableCollection<AgentChatItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>The empty-conversation invitation is shown only when chatting is actually possible.</summary>
    public bool ShowEmptyInvitation => IsEmpty && IsProviderUsable;

    public bool CanEditRequest => IsIdle && IsFeatureAvailable;

    public bool IsFeatureAvailable => _services.IsComplete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExternalDestination), nameof(DestinationText), nameof(DestinationHint),
        nameof(ConsentText), nameof(HasModels), nameof(IsStatusError), nameof(ModeText), nameof(HasModeText),
        nameof(ReadScopeText), nameof(HasReadScope))]
    private AgentProviderOption? _selectedProvider;

    [ObservableProperty] private string? _selectedModel;

    [ObservableProperty] private AgentContextScopeOption _selectedScope;

    [ObservableProperty] private bool _destinationConsent;

    [ObservableProperty] private string _composerText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private AgentChatPreview? _preview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsStatusError), nameof(IsBusy), nameof(IsIdle),
        nameof(CanChangeProvider), nameof(CanEditRequest), nameof(ShowEmptyInvitation))]
    private AgentChatState _state;

    [ObservableProperty] private string _tabContextText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _statusDetail;

    [ObservableProperty] private bool _isPreparingPreview;

    [ObservableProperty] private bool _isRefreshingProviders;

    public bool HasPreview => Preview is not null;

    public bool HasModels => Models.Count > 0;

    public bool IsExternalDestination => SelectedProvider?.IsExternal == true;

    public string DestinationText => SelectedProvider?.DestinationText ?? "";

    public string DestinationHint => SelectedProvider?.DestinationHint ?? "";

    public string ConsentText => Text.Format("agentConsentExternal", SelectedProvider?.Presentation.DisplayName ?? "");

    /// <summary>Mode chip next to Local/Externo ("Claude · assinatura" / "Claude · API"); derived from capabilities.</summary>
    public string? ModeText => SelectedProvider?.ModeText;

    public bool HasModeText => ModeText is not null;

    /// <summary>
    /// Permanent read notice of a CLI-delegated provider: the folder its native read tools may read in a new session
    /// (or none, when every read asks for approval) and that what is read goes to the recipient. Empty otherwise.
    /// </summary>
    public string ReadScopeText
    {
        get
        {
            if (CurrentReadScope() is not { } current)
            {
                return "";
            }

            var (scope, profile) = current;
            var text = AgentCliReadScopeText.Describe(scope, profile);
            if (scope.BlocksSending)
            {
                return text;
            }

            // Per-session snapshot: say that changes apply to new sessions and which folder the current one keeps.
            var pinned = HasSessionWorkingDirectory && _sessionProviderId == SelectedProvider!.ProviderId &&
                !string.Equals(SessionWorkingDirectory, scope.CandidateDirectory, StringComparison.Ordinal)
                    ? " " + Text.Format("agentCliReadScopeSessionPinned", SessionWorkingDirectory ?? Text.Resolve("agentCliReadScopeNoFolder"))
                    : "";
            return text + " " + Text.Resolve("agentCliReadScopeNewSessions") + pinned;
        }
    }

    /// <summary>Neither the chosen folder nor the dedicated folder can be used: no session can start (no fallback).</summary>
    public bool IsReadScopeBlocked => CurrentReadScope()?.Scope.BlocksSending == true;

    private (string ProviderId, string? Candidate, AgentCliReadScope Scope, AgentCliProviderProfile Profile)? _readScopeCache;

    /// <summary>
    /// Provider preview of the read scope for the folder currently in the Files panel (what a new session would use).
    /// Cached per provider/folder; <see cref="RefreshReadScope"/> re-evaluates it.
    /// </summary>
    private (AgentCliReadScope Scope, AgentCliProviderProfile Profile)? CurrentReadScope()
    {
        if (SelectedProvider is not { UsesOfficialCli: true } provider || _services.CliAccounts is not { } accounts)
        {
            return null;
        }

        var candidate = CaptureWorkspaceFolder();
        if (_readScopeCache is { } cached && cached.ProviderId == provider.ProviderId && cached.Candidate == candidate)
        {
            return (cached.Scope, cached.Profile);
        }

        try
        {
            if (accounts.Describe(provider.ProviderId) is not { } profile)
            {
                return null;
            }

            var scope = accounts.DescribeReadScope(provider.ProviderId, candidate);
            _readScopeCache = (provider.ProviderId, candidate, scope, profile);
            return (scope, profile);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool HasReadScope => ReadScopeText.Length > 0;

    /// <summary>Folder captured when the current session started (null = none); valid while <see cref="HasSessionWorkingDirectory"/>.</summary>
    public string? SessionWorkingDirectory { get; private set; }

    public bool HasSessionWorkingDirectory { get; private set; }

    private string? CaptureWorkspaceFolder()
    {
        try
        {
            var folder = _captureWorkspaceFolder?.Invoke();
            return string.IsNullOrWhiteSpace(folder) ? null : folder;
        }
        catch (Exception)
        {
            return null; // No folder: the provider uses its dedicated empty folder (reads ask for approval).
        }
    }

    /// <summary>Called when the Files panel folder changes: the notice always shows the folder a new session would use.</summary>
    public void RefreshReadScope()
    {
        _readScopeCache = null;
        OnPropertyChanged(nameof(ReadScopeText));
        OnPropertyChanged(nameof(HasReadScope));
        OnPropertyChanged(nameof(IsReadScopeBlocked));
        if (IsIdle)
        {
            UpdateIdleState();
            ReviewCommand.NotifyCanExecuteChanged();
            SendCommand.NotifyCanExecuteChanged();
            PrimaryActionCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsBusy => State is AgentChatState.Connecting or AgentChatState.Generating or AgentChatState.WaitingTool or
        AgentChatState.WaitingApproval or AgentChatState.Cancelling;

    public bool IsIdle => !IsBusy;

    public bool CanChangeProvider => IsIdle && IsFeatureAvailable;

    /// <summary>
    /// Error styling accompanies the text of failure states; a provider whose availability was simply not checked yet
    /// is a neutral state with its own action (Check availability), not an error.
    /// </summary>
    public bool IsStatusError => (State == AgentChatState.ProviderUnavailable && SelectedProvider?.IsNotChecked != true) ||
        State is AgentChatState.Unavailable or AgentChatState.NoProvider or AgentChatState.NotAuthenticated or AgentChatState.CredentialExpired or
        AgentChatState.VaultUnavailable or AgentChatState.OutcomeUnknown or AgentChatState.TimedOut or
        AgentChatState.Failed or AgentChatState.ContextFailed or AgentChatState.ReadScopeUnavailable;

    public string StatusText
    {
        get
        {
            var name = SelectedProvider?.Presentation.DisplayName ?? "";
            var text = State switch
            {
                AgentChatState.Unavailable => Text.Resolve("agentStateUnavailable"),
                AgentChatState.NoProvider => Text.Resolve("agentStateNoProvider"),
                AgentChatState.ProviderUnavailable => Text.Format("agentStateProviderUnavailable", name,
                    SelectedProvider?.UnavailableText ?? Text.Resolve("agentSettingsUnavailable")),
                AgentChatState.NotAuthenticated => Text.Format(UsesOfficialCli ? "agentStateCliSignedOut" : "agentStateNotAuthenticated", name),
                AgentChatState.CredentialExpired => UsesOfficialCli
                    ? Text.Format("agentStateCliBlocked", name, SelectedProvider?.UnavailableText ?? "")
                    : Text.Format("agentStateCredentialExpired", name),
                AgentChatState.VaultUnavailable => Text.Format("agentStateVaultUnavailable", name),
                AgentChatState.Ready => IsExternalDestination && !DestinationConsent
                    ? Text.Resolve("agentStateConsentRequired")
                    : Text.Resolve("agentStateReady"),
                AgentChatState.ReadScopeUnavailable => Text.Format("agentStateReadScopeUnavailable", name),
                AgentChatState.Reviewing => Text.Resolve("agentStateReviewing"),
                AgentChatState.Connecting => Text.Format("agentStateConnecting", name),
                AgentChatState.Generating => Text.Resolve("agentStateGenerating"),
                AgentChatState.WaitingTool => Text.Resolve("agentStateWaitingTool"),
                AgentChatState.WaitingApproval => Text.Resolve("agentStateWaitingApproval"),
                AgentChatState.Cancelling => Text.Resolve("agentStateCancelling"),
                AgentChatState.Completed => Text.Resolve("agentStateCompleted"),
                AgentChatState.Cancelled => Text.Resolve("agentStateCancelled"),
                AgentChatState.OutcomeUnknown => DescribedDetail() is { } uncertain
                    ? Text.Format("agentStateOutcomeUnknownDescribed", uncertain)
                    : Text.Resolve("agentStateOutcomeUnknown"),
                AgentChatState.TimedOut => Text.Resolve("agentStateTimedOut"),
                AgentChatState.ContextFailed => Text.Format("agentStateContextFailed", StatusDetail ?? "ContextUnavailable"),
                _ => StatusDetail is { } code && Text.HasTranslation(ErrorCodePrefix + code)
                    ? Text.Format("agentStateFailedDescribed", Text.Resolve(ErrorCodePrefix + code), code)
                    : Text.Format("agentStateFailed", StatusDetail ?? "AgentFailure"),
            };
            return State is AgentChatState.Ready && StatusDetail is { Length: > 0 } notice ? notice : text;
        }
    }

    /// <summary>Prefix of localized, provider-reported safe error codes (looked up by code, never by provider).</summary>
    public const string ErrorCodePrefix = "agentError.";

    private bool UsesOfficialCli => SelectedProvider?.UsesOfficialCli == true;

    /// <summary>Localized description of the current safe detail code, or null when this version does not know it.</summary>
    private string? DescribedDetail() =>
        StatusDetail is { Length: > 0 } code && Text.HasTranslation(ErrorCodePrefix + code) ? Text.Resolve(ErrorCodePrefix + code) : null;

    /// <summary>
    /// Executor named on native-tool cards: the official CLI profile of the turn's provider when the host describes one,
    /// otherwise its display name. Looked up by provider ID through the port, never by brand.
    /// </summary>
    private string? ObservedToolExecutor(TurnRun run)
    {
        var providerId = run.ProviderId;
        try
        {
            return _services.CliAccounts?.Describe(providerId)?.CliName ??
                Providers.FirstOrDefault(p => p.ProviderId == providerId)?.Presentation.DisplayName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Called by the host only when the tab's own context changes by explicit action (never by explorer selection).
    /// A reviewed package no longer matches and is discarded.
    /// </summary>
    public void RefreshTabContext()
    {
        var tab = _captureTab();
        TabContextText = Text.Format("agentFixedContext", DescribeTab(tab) ?? Text.Resolve("agentNoTabContext"));
        InvalidatePreview();
    }

    /// <summary>Re-reads provider availability, e.g. after the settings window closed. Keeps the current session.</summary>
    public void ReloadProviders() => LoadProviders(SelectedProvider?.ProviderId);

    /// <summary>
    /// Re-reads the catalog only when its cached listing changed since the last load (e.g. another tab checked
    /// availability), so returning to a tab does not reset a terminal status such as "Completed" for nothing.
    /// </summary>
    public void ReloadProvidersIfChanged()
    {
        if (!IsIdle || !_services.IsComplete)
        {
            return;
        }

        IReadOnlyList<AgentProviderPresentation> current;
        try
        {
            current = _services.Catalog!.List();
        }
        catch (Exception)
        {
            return;
        }

        if (!ReferenceEquals(current, _lastListing))
        {
            ReloadProviders();
        }
    }

    public AgentSettingsViewModel CreateSettingsViewModel() =>
        new(_services.Catalog, _services.Credentials, SelectedProvider?.ProviderId, _services.CliAccounts, _captureWorkspaceFolder);

    private static string? DescribeTab(AgentChatTabSnapshot tab)
    {
        var parts = new[] { tab.ConnectionLabel, tab.Database, tab.Collection }.Where(part => !string.IsNullOrWhiteSpace(part));
        var text = string.Join(" › ", parts);
        return text.Length == 0 ? null : text;
    }

    private void LoadProviders(string? keepProviderId)
    {
        var previous = SelectedProvider;
        _suppressInvalidation = true;
        try
        {
            Providers.Clear();
            IReadOnlyList<AgentProviderPresentation> listed = [];
            if (_services.IsComplete)
            {
                try
                {
                    listed = _services.Catalog!.List();
                    _lastListing = listed;
                }
                catch (Exception)
                {
                    listed = [];
                }
            }

            foreach (var item in listed.Where(static item => !string.IsNullOrWhiteSpace(item.ProviderId)))
            {
                Providers.Add(new AgentProviderOption(item));
            }

            // Only an explicit previous choice is kept; otherwise the first *available* provider is preselected so the
            // destination is visible. Preselection never grants consent to send.
            var match = keepProviderId is null ? null : Providers.FirstOrDefault(p => p.ProviderId == keepProviderId);
            SelectedProvider = match ?? Providers.FirstOrDefault(static p => p.Presentation.IsAvailable) ??
                Providers.FirstOrDefault();
        }
        finally
        {
            _suppressInvalidation = false;
        }

        if (!ReferenceEquals(previous, SelectedProvider) && previous?.ProviderId != SelectedProvider?.ProviderId)
        {
            OnProviderSwitched(previous);
        }
        else
        {
            LoadModels();
        }

        UpdateIdleState();
    }

    private void LoadModels()
    {
        var keep = SelectedModel;
        Models.Clear();
        foreach (var model in SelectedProvider?.Presentation.Models ?? [])
        {
            Models.Add(model);
        }

        SelectedModel = keep is not null && Models.Contains(keep) ? keep : Models.FirstOrDefault();
        OnPropertyChanged(nameof(HasModels));
    }

    partial void OnSelectedProviderChanged(AgentProviderOption? oldValue, AgentProviderOption? newValue)
    {
        if (_suppressInvalidation)
        {
            return;
        }

        OnProviderSwitched(oldValue);
        UpdateIdleState();
    }

    private void OnProviderSwitched(AgentProviderOption? previous)
    {
        // A different provider is a different recipient: a new session, no consent and no transferred conversation.
        DestinationConsent = false;
        LoadModels();
        InvalidatePreview();
        if (previous is not null && SelectedProvider is not null && previous.ProviderId != SelectedProvider.ProviderId)
        {
            _ = CloseSessionAsync();
            Items.Clear();
            StatusDetail = Text.Format("agentProviderSwitched", SelectedProvider.Presentation.DisplayName);
        }
    }

    partial void OnSelectedModelChanged(string? value) => InvalidatePreview();

    partial void OnSelectedScopeChanged(AgentContextScopeOption value) => InvalidatePreview();

    partial void OnDestinationConsentChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        SendCommand.NotifyCanExecuteChanged();
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnComposerTextChanged(string value)
    {
        InvalidatePreview();
        if (State is AgentChatState.Completed or AgentChatState.Cancelled or AgentChatState.OutcomeUnknown or
            AgentChatState.TimedOut or AgentChatState.Failed or AgentChatState.ContextFailed)
        {
            UpdateIdleState();
        }

        ReviewCommand.NotifyCanExecuteChanged();
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreviewChanged(AgentChatPreview? value)
    {
        ReviewCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnStateChanged(AgentChatState value)
    {
        ReviewCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged();
        CancelTurnCommand.NotifyCanExecuteChanged();
        PrimaryActionCommand.NotifyCanExecuteChanged();
        ConfigureCommand.NotifyCanExecuteChanged();
        RefreshProvidersCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowRefreshProviders));
    }

    private void InvalidatePreview()
    {
        if (_suppressInvalidation)
        {
            return;
        }

        Interlocked.Increment(ref _reviewGeneration);
        IsPreparingPreview = false;
        if (Preview is null)
        {
            return;
        }

        Preview = null;
        if (State == AgentChatState.Reviewing)
        {
            State = AgentChatState.Ready;
        }

        StatusDetail = Text.Resolve("agentPreviewStale");
    }

    /// <summary>Idle state derived from capabilities and auth state; never from a provider brand.</summary>
    private void UpdateIdleState()
    {
        if (IsBusy)
        {
            return;
        }

        // A credential problem is reported before generic unavailability because it has a concrete action
        // (Configure…), even when the provider is also unavailable for that very reason.
        State = !IsFeatureAvailable ? AgentChatState.Unavailable
            : SelectedProvider is not { } provider ? AgentChatState.NoProvider
            : provider.Presentation.AuthState switch
            {
                AgentProviderAuthState.NotConfigured => AgentChatState.NotAuthenticated,
                AgentProviderAuthState.Invalid or AgentProviderAuthState.Expired => AgentChatState.CredentialExpired,
                AgentProviderAuthState.VaultUnavailable => AgentChatState.VaultUnavailable,
                _ when !provider.Presentation.IsAvailable => AgentChatState.ProviderUnavailable,
                _ when IsReadScopeBlocked => AgentChatState.ReadScopeUnavailable,
                _ => Preview is null ? AgentChatState.Ready : AgentChatState.Reviewing,
            };
        OnPropertyChanged(nameof(ShowRefreshProviders));
    }

    /// <summary>"Check availability" is offered only while no usable provider is selected and nothing runs.</summary>
    public bool ShowRefreshProviders => IsFeatureAvailable && IsIdle && !IsProviderUsable && State != AgentChatState.ReadScopeUnavailable;

    private bool CanRefreshProviders() => IsFeatureAvailable && IsIdle && !IsRefreshingProviders;

    /// <summary>
    /// Explicit user action: re-checks local configuration and vault presence of every provider (never network or
    /// authentication). Reading the vault may show an unlock prompt, which is why listing alone never does it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefreshProviders))]
    private async Task RefreshProvidersAsync()
    {
        IsRefreshingProviders = true;
        StatusDetail = null;
        var failed = false;
        try
        {
            await _services.Catalog!.RefreshAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            failed = true;
        }
        finally
        {
            IsRefreshingProviders = false;
        }

        ReloadProviders();
        if (failed)
        {
            StatusDetail = Text.Resolve("agentRefreshFailed");
        }
    }

    partial void OnIsRefreshingProvidersChanged(bool value) => RefreshProvidersCommand.NotifyCanExecuteChanged();

    private bool IsProviderUsable =>
        IsFeatureAvailable && SelectedProvider is { Presentation: { IsAvailable: true } presentation } &&
        presentation.AuthState is AgentProviderAuthState.NotRequired or AgentProviderAuthState.Configured && !IsReadScopeBlocked;

    private bool CanReview() =>
        IsProviderUsable && IsIdle && Preview is null && !IsPreparingPreview && !string.IsNullOrWhiteSpace(ComposerText);

    /// <summary>Captures the tab synchronously, then builds the consent-filtered snapshot for the user to review.</summary>
    [RelayCommand(CanExecute = nameof(CanReview))]
    private async Task ReviewAsync()
    {
        // Snapshot before any await: provider, model, scope, message and tab context are fixed here.
        var tab = _captureTab();
        var provider = SelectedProvider!;
        var model = SelectedModel;
        var scope = SelectedScope.Scope;
        var message = ComposerText;
        StatusDetail = null;
        if (scope == AgentContextScope.Selection && string.IsNullOrEmpty(tab.SelectedText))
        {
            StatusDetail = Text.Resolve("agentPreviewSelectionEmpty");
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        var shareNamespace = scope != AgentContextScope.None;
        var request = new AgentContextCaptureRequest(tab.TabId, tab.DocumentVersion,
            shareNamespace ? tab.ConnectionId : null, shareNamespace ? tab.Database : null,
            shareNamespace ? tab.Collection : null, EditorText: null,
            SelectedText: scope == AgentContextScope.Selection ? tab.SelectedText : null);
        var generation = Interlocked.Increment(ref _reviewGeneration);
        IsPreparingPreview = true;
        ReviewCommand.NotifyCanExecuteChanged();
        AgentContextSnapshot snapshot;
        try
        {
            snapshot = await _services.ContextProvider!.CaptureAsync(request, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            if (generation == Interlocked.Read(ref _reviewGeneration))
            {
                IsPreparingPreview = false;
                StatusDetail = exception is AgentRuntimeException runtime ? SafeCode(runtime.Code) : "ContextUnavailable";
                State = AgentChatState.ContextFailed;
            }

            return;
        }

        if (generation != Interlocked.Read(ref _reviewGeneration))
        {
            return; // Message, scope, provider or tab context changed while capturing: this snapshot is stale.
        }

        IsPreparingPreview = false;
        if (snapshot is null || snapshot.TabId != tab.TabId || snapshot.DocumentVersion != tab.DocumentVersion)
        {
            StatusDetail = "ContextMismatch";
            State = AgentChatState.ContextFailed;
            return;
        }

        var namespaceText = string.Join(" › ", new[] { snapshot.ConnectionId, snapshot.DatabaseName, snapshot.CollectionName }
            .Where(static part => !string.IsNullOrWhiteSpace(part)));
        Preview = new AgentChatPreview(provider.ProviderId, model, scope, message, tab, snapshot,
            provider.ModeText is { } mode
                ? Text.Format("agentPreviewDestinationMode", provider.Presentation.DisplayName, provider.DestinationText, mode)
                : Text.Format("agentPreviewDestination", provider.Presentation.DisplayName, provider.DestinationText),
            Text.Format("agentPreviewScope", SelectedScope.Label),
            Text.Format("agentPreviewNamespace", namespaceText.Length == 0 ? "—" : namespaceText),
            string.IsNullOrEmpty(snapshot.AuthorizedContext) ? Text.Resolve("agentPreviewContextEmpty") : snapshot.AuthorizedContext);
        State = AgentChatState.Reviewing;
    }

    private bool CanEdit() => Preview is not null && IsIdle;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        Interlocked.Increment(ref _reviewGeneration);
        Preview = null;
        StatusDetail = null;
        UpdateIdleState();
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanConfigure() => IsIdle && IsFeatureAvailable;

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private void Configure() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private bool CanPrimaryAction() => CanSend() || CanReview();

    /// <summary>Ctrl+Enter in the composer: review first, then send the reviewed package.</summary>
    [RelayCommand(CanExecute = nameof(CanPrimaryAction))]
    private Task PrimaryActionAsync() => CanSend() ? SendCommand.ExecuteAsync(null) : ReviewCommand.ExecuteAsync(null);

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalizationViewModel.Language))
        {
            return;
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DestinationText));
        OnPropertyChanged(nameof(DestinationHint));
        OnPropertyChanged(nameof(ConsentText));
        OnPropertyChanged(nameof(ModeText));
        RefreshReadScope();
        TabContextText = Text.Format("agentFixedContext", DescribeTab(_captureTab()) ?? Text.Resolve("agentNoTabContext"));
    }

    private static string SafeCode(string? code) =>
        code is { Length: > 0 and <= 64 } && code.All(char.IsAsciiLetterOrDigit) ? code : "AgentFailure";

    public async ValueTask DisposeAsync()
    {
        LocalizationViewModel.Current.PropertyChanged -= OnLocalizationChanged;
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        await _lifetime.CancelAsync();
        await CloseSessionAsync();
        _lifetime.Dispose();
    }
}
