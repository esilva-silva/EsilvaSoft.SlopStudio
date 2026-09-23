using EsilvaSoft.SlopStudio.LocalAi.Core;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    [ObservableProperty] private string _chatInput = "";
    [ObservableProperty] private AiChatStatus _chatStatus;
    [ObservableProperty] private string _chatStatusMessage = LocalizationViewModel.Current.Resolve("chatIntro");
    [ObservableProperty] private AiChangeProposal? _aiProposal;
    [ObservableProperty] private string _aiContextPreview = "";
    [ObservableProperty] private bool _hasAiContextPreview;
    private CancellationTokenSource? _chatCancellation;
    private long _chatGeneration;
    private long _aiEditorRevision;
    private long _proposalEditorRevision;
    private long _aiInputRevision;
    private long _proposalInputRevision;
    private long _aiOriginRevision;
    private long _proposalOriginRevision;
    private bool _aiOriginObserverAttached;
    private Guid? _proposalProfileId;
    private string _proposalDatabase = "";
    private string _proposalCollection = "";
    private string _proposalMode = "";
    private AutocompleteSettings? _proposalPolicy;
    private AiContextReview? _pendingContextReview;
    private EventHandler? _previewPolicyChanged;
    private IAutocompleteService? _previewPolicySource;
    private sealed record AiContextReview(AiEditorContext Context, string Instruction, string Original, long EditorRevision,
        long InputRevision, long OriginRevision, Guid? ProfileId, string Database, string Collection, string Mode, AutocompleteSettings Policy);

    public IAiChatService AiChat { get; set; } = new AiChatService();
    public ObservableCollection<AiChatMessage> ChatMessages { get; } = [];
    public bool IsChatBusy => ChatStatus == AiChatStatus.Loading;
    public bool HasAiProposal => AiProposal is not null;
    public bool ShowChatStatus => !HasAiProposal && !HasAiContextPreview;
    public bool CanSendAiChat => !IsChatBusy && !string.IsNullOrWhiteSpace(ChatInput);
    public bool HasChatMessages => ChatMessages.Count > 0;
    public string AiProposalTitle => AiProposal?.IsNoOp == true ? LocalizationViewModel.Current.Resolve("noChanges") : LocalizationViewModel.Current.Resolve("proposalToReview");
    public string AiProposalHint => AiProposal is { RequiresAdditionalConfirmation: true }
        ? LocalizationViewModel.Current.Resolve("aiProposalSafety")
        : LocalizationViewModel.Current.Resolve("aiProposalReviewHint");

    partial void OnChatInputChanged(string value) => OnPropertyChanged(nameof(CanSendAiChat));
    partial void OnTextChanged(string value) => Interlocked.Increment(ref _aiEditorRevision);
    partial void OnInputJsonChanged(string value) => Interlocked.Increment(ref _aiInputRevision);
    partial void OnChatStatusChanged(AiChatStatus value)
    {
        OnPropertyChanged(nameof(IsChatBusy));
        OnPropertyChanged(nameof(CanSendAiChat));
    }
    partial void OnAiProposalChanged(AiChangeProposal? value)
    {
        if (value is not null)
        {
            _proposalEditorRevision = Interlocked.Read(ref _aiEditorRevision);
            _proposalInputRevision = Interlocked.Read(ref _aiInputRevision);
            _proposalOriginRevision = Interlocked.Read(ref _aiOriginRevision);
            _proposalProfileId = Profile?.Id;
            _proposalDatabase = Database;
            _proposalCollection = Collection;
            _proposalMode = Mode;
            _proposalPolicy = Autocomplete.Settings;
        }
        OnPropertyChanged(nameof(HasAiProposal));
        OnPropertyChanged(nameof(ShowChatStatus));
        OnPropertyChanged(nameof(AiProposalTitle));
        OnPropertyChanged(nameof(AiProposalHint));
    }

    public AiEditorContext CaptureAiEditorContext(string instruction) => CaptureAiEditorContext(instruction, Autocomplete.Settings);

    private AiEditorContext CaptureAiEditorContext(string instruction, AutocompleteSettings policy)
    {
        ObserveAiContextOrigin();
        var operation = policy.UseEditorContext ? InferOperationType(Text, Mode) : "consulta";
        var fields = policy.UseResultPanelContext
            ? MqlAutocompleteService.InferFieldPaths(ResultSegments.Take(8).Select(segment =>
                Results.Substring(segment.Start, Math.Min(segment.Length, 8192)))).Take(128).ToArray()
            : [];
        var safety = LocalizationViewModel.Current.Resolve("aiContextSafety");
        var fieldsContext = fields.Length == 0 ? null
            : LocalizationViewModel.Current.Format("aiContextFields", string.Join(", ", fields));
        var extraParts = new List<string>();
        if (fieldsContext is not null) extraParts.Add(fieldsContext);
        extraParts.Add(safety);
        if (policy.IncludeInputJsonInLocalAiContext && !string.IsNullOrWhiteSpace(InputJson))
        {
            var input = "INPUT JSON (opt-in):\n" + InputJson;
            var currentLength = string.Join("\n", extraParts).Length;
            if (currentLength + 1 + input.Length > 8192)
                throw new InvalidOperationException(LocalizationViewModel.Current.Resolve("aiInputContextTooLarge"));
            extraParts.Add(input);
        }
        var extra = string.Join("\n", extraParts);
        return new AiEditorContext(instruction, policy.UseEditorContext ? Title : "", policy.UseEditorContext ? Text : "", "javascript", "mongosh",
            Database, Collection, operation, extra).Bounded();
    }
    partial void OnHasAiContextPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSendAiChat));
        OnPropertyChanged(nameof(ShowChatStatus));
    }

    private void ObserveAiContextOrigin()
    {
        if (_aiOriginObserverAttached) return;
        _aiOriginObserverAttached = true;
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Profile) or nameof(Database) or nameof(Collection) or nameof(Mode) or nameof(Results))
                Interlocked.Increment(ref _aiOriginRevision);
        };
    }

    [RelayCommand(CanExecute = nameof(CanSendAiChat))]
    private async Task SendAiChatAsync()
    {
        var instruction = ChatInput.Trim();
        if (instruction.Length == 0 || IsChatBusy) return;
        ObserveAiContextOrigin();
        var autocomplete = Autocomplete;
        var policy = autocomplete.Settings;
        if (!policy.LocalAiContextEnabled || Profile is { LocalAiContextEnabled: false })
        {
            ChatStatus = AiChatStatus.NoContext;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiContextDisabled");
            return;
        }
        if (policy.UseEditorContext && Text.Length > 1_048_576)
        {
            ChatStatus = AiChatStatus.Error;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiEditorContextTooLarge");
            return;
        }
        if (policy.IncludeInputJsonInLocalAiContext && InputJson.Length > 8192)
        {
            ChatStatus = AiChatStatus.Error;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiInputContextTooLarge");
            return;
        }
        AiEditorContext context;
        if (_pendingContextReview is { } reviewed)
        {
            if (reviewed.Instruction != instruction || reviewed.Policy != policy || reviewed.ProfileId != Profile?.Id
                || reviewed.Database != Database || reviewed.Collection != Collection || reviewed.Mode != Mode
                || reviewed.Original != Text || reviewed.EditorRevision != Interlocked.Read(ref _aiEditorRevision)
                || reviewed.InputRevision != Interlocked.Read(ref _aiInputRevision)
                || reviewed.OriginRevision != Interlocked.Read(ref _aiOriginRevision))
            {
                ClearAiContextPreview();
                ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiContextPreviewChanged");
                return;
            }
            context = reviewed.Context;
            ClearAiContextPreview();
        }
        else
        {
            try { context = CaptureAiEditorContext(instruction, policy); }
            catch (InvalidOperationException ex)
            {
                ChatStatus = AiChatStatus.Error;
                ChatStatusMessage = ex.Message;
                return;
            }
            if (!context.HasContext)
            {
                ChatStatus = AiChatStatus.NoContext;
                ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiNoContext");
                return;
            }
            _pendingContextReview = new(context, instruction, Text, Interlocked.Read(ref _aiEditorRevision),
                Interlocked.Read(ref _aiInputRevision), Interlocked.Read(ref _aiOriginRevision), Profile?.Id,
                Database, Collection, Mode, policy);
            _previewPolicySource = autocomplete;
            _previewPolicyChanged = (_, _) => Interlocked.Increment(ref _aiOriginRevision);
            autocomplete.SettingsChanged += _previewPolicyChanged;
            AiContextPreview = FormatAiContextPreview(context);
            HasAiContextPreview = true;
            ChatStatus = AiChatStatus.Idle;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiContextPreviewReview");
            return;
        }
        if (!context.HasContext)
        {
            ChatStatus = AiChatStatus.NoContext;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiNoContext");
            return;
        }

        var generation = ++_chatGeneration;
        var original = Text;
        var editorRevision = Interlocked.Read(ref _aiEditorRevision);
        var inputRevision = Interlocked.Read(ref _aiInputRevision);
        var originRevision = Interlocked.Read(ref _aiOriginRevision);
        var profile = Profile;
        var database = Database;
        var collection = Collection;
        var mode = Mode;
        var policyChanged = new EventHandler((_, _) => Interlocked.Increment(ref _aiOriginRevision));
        autocomplete.SettingsChanged += policyChanged;
        using var cancellation = new CancellationTokenSource();
        _chatCancellation?.Cancel();
        _chatCancellation = cancellation;
        ChatMessages.Add(new AiChatMessage("user", instruction, DateTimeOffset.Now));
        OnPropertyChanged(nameof(HasChatMessages));
        ChatInput = "";
        AiProposal = null;
        ChatStatus = AiChatStatus.Loading;
        ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiAnalyzing");
        try
        {
            var response = await AiChat.AskAsync(new AiChatRequest(context), cancellation.Token).ConfigureAwait(true);
            if (generation != _chatGeneration || !ReferenceEquals(Profile, profile) || Database != database || Collection != collection || Mode != mode
                || Text != original || Interlocked.Read(ref _aiEditorRevision) != editorRevision
                || Interlocked.Read(ref _aiInputRevision) != inputRevision
                || Interlocked.Read(ref _aiOriginRevision) != originRevision
                || Autocomplete.Settings != policy || profile?.LocalAiContextEnabled == false)
            {
                ChatStatus = AiChatStatus.Error;
                ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiStaleResponse");
                return;
            }
            if (response is null || response.IsEmpty || string.IsNullOrWhiteSpace(response.ProposedCode)
                || response.ProposedCode.Length >= 1_048_576)
            {
                ChatStatus = AiChatStatus.Empty;
                ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiEmptyResponse");
                return;
            }
            var validation = await ValidateCodeAsync(response.ProposedCode, mode == "Agregação", cancellation.Token).ConfigureAwait(true);
            if (generation != _chatGeneration || !ReferenceEquals(Profile, profile) || Database != database || Collection != collection
                || Mode != mode || Text != original || Interlocked.Read(ref _aiEditorRevision) != editorRevision
                || Interlocked.Read(ref _aiInputRevision) != inputRevision || Interlocked.Read(ref _aiOriginRevision) != originRevision
                || Autocomplete.Settings != policy || profile?.LocalAiContextEnabled == false)
            {
                ChatStatus = AiChatStatus.Error;
                ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiStaleResponse");
                return;
            }
            if (!validation.IsValid)
            {
                ChatStatus = AiChatStatus.Error;
                ChatStatusMessage = LocalizationViewModel.Current.Format("aiInvalidProposal", validation.Message);
                return;
            }
            ChatMessages.Add(new AiChatMessage("assistant", response.Explanation, DateTimeOffset.Now));
            OnPropertyChanged(nameof(HasChatMessages));
            AiProposal = new AiChangeProposal(Guid.NewGuid(), original, response.ProposedCode, response.Explanation,
                AiDiffBuilder.Build(original, response.ProposedCode),
                response.RequiresAdditionalConfirmation, response.Warning, context);
            ChatStatus = AiChatStatus.Ready;
            ChatStatusMessage = response.RequiresAdditionalConfirmation
                ? LocalizationViewModel.Current.Resolve("aiProposalReadyConfirm")
                : LocalizationViewModel.Current.Resolve("aiProposalReady");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ChatStatus = AiChatStatus.Canceled;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiAnalysisCancelled");
        }
        catch (Exception ex)
        {
            ChatStatus = AiChatStatus.Error;
            ChatStatusMessage = LocalizationViewModel.Current.Format("aiQueryFailed", ex.Message);
        }
        finally
        {
            autocomplete.SettingsChanged -= policyChanged;
            if (ReferenceEquals(_chatCancellation, cancellation)) _chatCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelAiChat()
    {
        if (!IsChatBusy) return;
        _chatGeneration++;
        _chatCancellation?.Cancel();
        ChatStatus = AiChatStatus.Canceled;
        ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiCanceling");
    }

    public bool CanApplyAiProposal(AiChangeProposal proposal) =>
        IsCurrentProposal(proposal)
        && Text == proposal.OriginalContent
        && Interlocked.Read(ref _aiEditorRevision) == _proposalEditorRevision
        && Interlocked.Read(ref _aiInputRevision) == _proposalInputRevision
        && Interlocked.Read(ref _aiOriginRevision) == _proposalOriginRevision
        && !IsChatBusy;

    /// <summary>Commits a proposal only after the view has received explicit confirmation.</summary>
    public bool CommitAiProposal(AiChangeProposal proposal)
    {
        var revision = Interlocked.Read(ref _aiEditorRevision);
        var appliedThroughEditor = Text == proposal.ProposedContent && revision == _proposalEditorRevision + 1;
        if ((!CanApplyAiProposal(proposal) && !appliedThroughEditor) || !IsCurrentProposal(proposal))
        {
            ChatStatus = AiChatStatus.Error;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiProposalStale");
            AiProposal = null;
            return false;
        }
        Text = proposal.ProposedContent;
        AiProposal = null;
        ChatStatus = AiChatStatus.Ready;
        ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiProposalApplied");
        Status = LocalizationViewModel.Current.Resolve("aiStatusApplied");
        return true;
    }

    public bool ApplyAiProposal(AiChangeProposal proposal, bool additionalConfirmation = false)
    {
        if (proposal.RequiresAdditionalConfirmation && !additionalConfirmation)
        {
            ChatStatus = AiChatStatus.Ready;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiAdditionalConfirmation");
            return false;
        }
        return CommitAiProposal(proposal);
    }

    private static string FormatAiContextPreview(AiEditorContext context)
    {
        var localize = LocalizationViewModel.Current.Resolve;
        return string.Join("\n\n", new[]
        {
            ("aiPreviewInstruction", context.Instruction),
            ("aiPreviewHeader", context.Header),
            ("aiPreviewEditor", context.EditorContent),
            ("aiPreviewDatabase", context.Database),
            ("aiPreviewCollection", context.Collection),
            ("aiPreviewOperation", context.OperationType),
            ("aiPreviewLanguage", context.Language),
            ("aiPreviewDialect", context.Dialect),
            ("aiPreviewAdditionalContext", context.AdditionalContext)
        }.Select(section => $"{localize(section.Item1)}\n{section.Item2}"));
    }

    private void ClearAiContextPreview()
    {
        if (_previewPolicySource is not null && _previewPolicyChanged is not null)
            _previewPolicySource.SettingsChanged -= _previewPolicyChanged;
        _previewPolicySource = null;
        _previewPolicyChanged = null;
        _pendingContextReview = null;
        AiContextPreview = "";
        HasAiContextPreview = false;
    }

    [RelayCommand]
    private void EditAiContextPreview() => ClearAiContextPreview();

    private bool IsCurrentProposal(AiChangeProposal proposal)
    {
        if (AiProposal is not { } current || current != proposal || proposal.IsNoOp
            || proposal.ProposedContent.Length >= 1_048_576
            || !string.Equals(proposal.Diff, AiDiffBuilder.Build(proposal.OriginalContent, proposal.ProposedContent), StringComparison.Ordinal)
            || _proposalProfileId != Profile?.Id || _proposalDatabase != Database || _proposalCollection != Collection || _proposalMode != Mode
            || Interlocked.Read(ref _aiInputRevision) != _proposalInputRevision
            || Interlocked.Read(ref _aiOriginRevision) != _proposalOriginRevision
            || Autocomplete.Settings != _proposalPolicy || Profile is { LocalAiContextEnabled: false })
            return false;

        // Re-evaluate the exact policy/context captured by the request, including the separate Input JSON opt-in.
        var now = CaptureAiEditorContext(proposal.Context.Instruction, _proposalPolicy!) with
        {
            EditorContent = _proposalPolicy!.UseEditorContext ? proposal.OriginalContent : "",
            Header = proposal.Context.Header
        };
        return now == proposal.Context;
    }

    [RelayCommand]
    private void DismissAiProposal()
    {
        AiProposal = null;
        ChatStatus = AiChatStatus.Idle;
        ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiProposalDismissed");
    }

    private static string InferOperationType(string text, string mode)
    {
        if (mode == "Script") return "script";
        if (Regex.IsMatch(text, @"\.(?:insert|insertOne|insertMany)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "inserção";
        if (Regex.IsMatch(text, @"\.(?:delete|deleteOne|deleteMany|drop|dropDatabase)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "exclusão";
        if (Regex.IsMatch(text, @"\.(?:update|updateOne|updateMany|replace|replaceOne|bulkWrite)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "atualização";
        return "consulta";
    }
}
