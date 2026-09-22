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
    private CancellationTokenSource? _chatCancellation;
    private long _chatGeneration;

    public IAiChatService AiChat { get; set; } = new AiChatService();
    public ObservableCollection<AiChatMessage> ChatMessages { get; } = [];
    public bool IsChatBusy => ChatStatus == AiChatStatus.Loading;
    public bool HasAiProposal => AiProposal is not null;
    public bool CanSendAiChat => !IsChatBusy && !string.IsNullOrWhiteSpace(ChatInput);
    public bool HasChatMessages => ChatMessages.Count > 0;
    public string AiProposalTitle => AiProposal?.IsNoOp == true ? LocalizationViewModel.Current.Resolve("noChanges") : LocalizationViewModel.Current.Resolve("proposalToReview");
    public string AiProposalHint => AiProposal is { RequiresAdditionalConfirmation: true }
        ? LocalizationViewModel.Current.Resolve("aiProposalSafety")
        : LocalizationViewModel.Current.Resolve("aiProposalReviewHint");

    partial void OnChatInputChanged(string value) => OnPropertyChanged(nameof(CanSendAiChat));
    partial void OnChatStatusChanged(AiChatStatus value)
    {
        OnPropertyChanged(nameof(IsChatBusy));
        OnPropertyChanged(nameof(CanSendAiChat));
    }
    partial void OnAiProposalChanged(AiChangeProposal? value)
    {
        OnPropertyChanged(nameof(HasAiProposal));
        OnPropertyChanged(nameof(AiProposalTitle));
        OnPropertyChanged(nameof(AiProposalHint));
    }

    public AiEditorContext CaptureAiEditorContext(string instruction)
    {
        var operation = InferOperationType(Text, Mode);
        var fields = MqlAutocompleteService.InferFieldPaths(ResultSegments.Take(8).Select(segment =>
            Results.Substring(segment.Start, Math.Min(segment.Length, 8192)))).Take(128).ToArray();
        var extra = string.Join("\n", new[]
        {
            string.IsNullOrWhiteSpace(Profile?.Name) ? null : LocalizationViewModel.Current.Format("aiContextConnection", Profile.Name),
            string.IsNullOrWhiteSpace(Context) ? null : LocalizationViewModel.Current.Format("aiContextTarget", Context),
            fields.Length == 0 ? null : LocalizationViewModel.Current.Format("aiContextFields", string.Join(", ", fields)),
            LocalizationViewModel.Current.Resolve("aiContextSafety")
        }.Where(value => value is not null));
        return new AiEditorContext(instruction, Title, Text, "javascript", "mongosh",
            Database, Collection, operation, extra).Bounded();
    }

    [RelayCommand(CanExecute = nameof(CanSendAiChat))]
    private async Task SendAiChatAsync()
    {
        var instruction = ChatInput.Trim();
        if (instruction.Length == 0 || IsChatBusy) return;
        var context = CaptureAiEditorContext(instruction);
        if (!context.HasContext)
        {
            ChatStatus = AiChatStatus.NoContext;
            ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiNoContext");
            return;
        }

        var generation = ++_chatGeneration;
        var original = context.EditorContent;
        var profile = Profile;
        var database = Database;
        var collection = Collection;
        var mode = Mode;
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
            if (generation != _chatGeneration || !ReferenceEquals(Profile, profile) || Database != database || Collection != collection || Mode != mode || Text != original)
            {
                ChatStatus = AiChatStatus.Error;
                ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiStaleResponse");
                return;
            }
            if (response is null || response.IsEmpty || string.IsNullOrWhiteSpace(response.ProposedCode))
            {
                ChatStatus = AiChatStatus.Empty;
                ChatStatusMessage = LocalizationViewModel.Current.Resolve("aiEmptyResponse");
                return;
            }
            ChatMessages.Add(new AiChatMessage("assistant", response.Explanation, DateTimeOffset.Now));
            OnPropertyChanged(nameof(HasChatMessages));
            AiProposal = new AiChangeProposal(Guid.NewGuid(), original, response.ProposedCode, response.Explanation, response.Diff,
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
        AiProposal is { } current && current.Id == proposal.Id && Text == proposal.OriginalContent && !IsChatBusy;

    /// <summary>Commits a proposal only after the view has received explicit confirmation.</summary>
    public bool CommitAiProposal(AiChangeProposal proposal)
    {
        if (!CanApplyAiProposal(proposal) && !(AiProposal?.Id == proposal.Id && Text == proposal.ProposedContent))
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
