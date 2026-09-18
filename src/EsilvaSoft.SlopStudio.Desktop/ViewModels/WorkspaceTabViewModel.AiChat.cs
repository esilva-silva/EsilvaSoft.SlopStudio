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
    [ObservableProperty] private string _chatStatusMessage = "Faça uma pergunta sobre o código desta aba.";
    [ObservableProperty] private AiChangeProposal? _aiProposal;
    private CancellationTokenSource? _chatCancellation;
    private long _chatGeneration;

    public IAiChatService AiChat { get; set; } = new AiChatService();
    public ObservableCollection<AiChatMessage> ChatMessages { get; } = [];
    public bool IsChatBusy => ChatStatus == AiChatStatus.Loading;
    public bool HasAiProposal => AiProposal is not null;
    public bool CanSendAiChat => !IsChatBusy && !string.IsNullOrWhiteSpace(ChatInput);
    public bool HasChatMessages => ChatMessages.Count > 0;
    public string AiProposalTitle => AiProposal?.IsNoOp == true ? "Nenhuma alteração" : "Proposta para revisar";
    public string AiProposalHint => AiProposal is { RequiresAdditionalConfirmation: true }
        ? "Esta proposta contém escrita ou operação destrutiva. A aplicação exigirá uma confirmação adicional e não executará o código."
        : "Revise a proposta. Ela só será inserida no editor depois da sua confirmação.";

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
            string.IsNullOrWhiteSpace(Profile?.Name) ? null : "conexão: " + Profile.Name,
            string.IsNullOrWhiteSpace(Context) ? null : "destino visual: " + Context,
            fields.Length == 0 ? null : "campos conhecidos nos resultados: " + string.Join(", ", fields),
            "não executar consultas nem alterar o Explorer durante a revisão"
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
            ChatStatusMessage = "Escolha um banco ou escreva conteúdo no editor para fornecer contexto à IA.";
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
        ChatStatusMessage = "Analisando a instrução e o contexto da aba…";
        try
        {
            var response = await AiChat.AskAsync(new AiChatRequest(context), cancellation.Token).ConfigureAwait(true);
            if (generation != _chatGeneration || !ReferenceEquals(Profile, profile) || Database != database || Collection != collection || Mode != mode || Text != original)
            {
                ChatStatus = AiChatStatus.Error;
                ChatStatusMessage = "O editor ou o destino mudou enquanto a IA respondia. A proposta antiga foi descartada.";
                return;
            }
            if (response is null || response.IsEmpty || string.IsNullOrWhiteSpace(response.ProposedCode))
            {
                ChatStatus = AiChatStatus.Empty;
                ChatStatusMessage = "A IA não retornou uma proposta para esta instrução.";
                return;
            }
            ChatMessages.Add(new AiChatMessage("assistant", response.Explanation, DateTimeOffset.Now));
            OnPropertyChanged(nameof(HasChatMessages));
            AiProposal = new AiChangeProposal(Guid.NewGuid(), original, response.ProposedCode, response.Explanation, response.Diff,
                response.RequiresAdditionalConfirmation, response.Warning, context);
            ChatStatus = AiChatStatus.Ready;
            ChatStatusMessage = response.RequiresAdditionalConfirmation
                ? "Proposta pronta. Leia o alerta e confirme novamente antes de aplicar."
                : "Proposta pronta para revisão. Nada foi alterado no editor.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ChatStatus = AiChatStatus.Canceled;
            ChatStatusMessage = "Análise cancelada. O editor não foi alterado.";
        }
        catch (Exception ex)
        {
            ChatStatus = AiChatStatus.Error;
            ChatStatusMessage = "Não foi possível consultar a IA: " + ex.Message;
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
        ChatStatusMessage = "Cancelando a análise… o editor permanece intacto.";
    }

    public bool CanApplyAiProposal(AiChangeProposal proposal) =>
        AiProposal is { } current && current.Id == proposal.Id && Text == proposal.OriginalContent && !IsChatBusy;

    /// <summary>Commits a proposal only after the view has received explicit confirmation.</summary>
    public bool CommitAiProposal(AiChangeProposal proposal)
    {
        if (!CanApplyAiProposal(proposal) && !(AiProposal?.Id == proposal.Id && Text == proposal.ProposedContent))
        {
            ChatStatus = AiChatStatus.Error;
            ChatStatusMessage = "A proposta ficou desatualizada porque o editor mudou. Gere uma nova proposta.";
            AiProposal = null;
            return false;
        }
        Text = proposal.ProposedContent;
        AiProposal = null;
        ChatStatus = AiChatStatus.Ready;
        ChatStatusMessage = "Proposta aplicada ao editor. Use Ctrl+Z para desfazer.";
        Status = "Proposta de IA aplicada";
        return true;
    }

    public bool ApplyAiProposal(AiChangeProposal proposal, bool additionalConfirmation = false)
    {
        if (proposal.RequiresAdditionalConfirmation && !additionalConfirmation)
        {
            ChatStatus = AiChatStatus.Ready;
            ChatStatusMessage = "Esta proposta exige uma confirmação adicional porque contém escrita ou operação destrutiva.";
            return false;
        }
        return CommitAiProposal(proposal);
    }

    [RelayCommand]
    private void DismissAiProposal()
    {
        AiProposal = null;
        ChatStatus = AiChatStatus.Idle;
        ChatStatusMessage = "Proposta descartada. O editor não foi alterado.";
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
