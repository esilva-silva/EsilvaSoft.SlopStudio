using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class AgentChatViewModel
{
    private AgentSessionId? _sessionId;
    private string? _sessionProviderId;
    private string? _sessionModelId;
    private TurnRun? _turn;

    /// <summary>State of one turn. Each turn owns its CTS; nothing is shared with other tabs or turns.</summary>
    private sealed class TurnRun(AgentTurnId turnId, CancellationTokenSource cancellation)
    {
        public AgentTurnId TurnId { get; } = turnId;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public AgentSessionId? SessionId { get; set; }

        public string? ErrorCode { get; set; }

        public Dictionary<AgentMessageId, AgentChatMessageItem> Messages { get; } = [];

        public Dictionary<AgentToolCallId, AgentToolCallItem> Tools { get; } = [];

        public Dictionary<AgentApprovalId, AgentApprovalCardItem> Approvals { get; } = [];
    }

    /// <summary>Completion of the running turn, for hosts and tests; null when idle.</summary>
    public Task? CurrentTurnCompletion { get; private set; }

    public AgentTurnId? ActiveTurnId => _turn?.TurnId;

    private bool CanSend() =>
        Preview is { } preview && IsIdle && IsProviderUsable && SelectedProvider!.ProviderId == preview.ProviderId &&
        (!IsExternalDestination || DestinationConsent);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendAsync()
    {
        var preview = Preview!;
        // The reviewed package must still describe the tab: any change since the review requires a new preview.
        var current = _captureTab();
        if (!MatchesReviewedTab(preview, current))
        {
            InvalidatePreview();
            return Task.CompletedTask;
        }

        var run = new TurnRun(AgentTurnId.New(), CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
        var request = preview.Snapshot.ToTurnRequest(run.TurnId, preview.Message);
        _turn = run;
        _suppressInvalidation = true;
        try
        {
            Preview = null;
            ComposerText = "";
            StatusDetail = null;
        }
        finally
        {
            _suppressInvalidation = false;
        }

        Items.Add(new AgentChatMessageItem(AgentChatRole.User, preview.Message));
        State = AgentChatState.Connecting;
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        var completion = RunTurnAsync(run, preview.ProviderId, preview.ModelId, request);
        CurrentTurnCompletion = completion;
        return completion;
    }

    private static bool MatchesReviewedTab(AgentChatPreview preview, AgentChatTabSnapshot current)
    {
        var reviewed = preview.Tab;
        if (reviewed.TabId != current.TabId)
        {
            return false;
        }

        if (preview.Scope != AgentContextScope.None &&
            (reviewed.ConnectionId != current.ConnectionId || reviewed.Database != current.Database ||
             reviewed.Collection != current.Collection))
        {
            return false;
        }

        return preview.Scope != AgentContextScope.Selection ||
               (reviewed.DocumentVersion == current.DocumentVersion && reviewed.SelectedText == current.SelectedText);
    }

    private async Task RunTurnAsync(TurnRun run, string providerId, string? modelId, AgentTurnRequest request)
    {
        var runtime = _services.Runtime!;
        try
        {
            var sessionId = await EnsureSessionAsync(runtime, providerId, modelId, run.Cancellation.Token);
            run.SessionId = sessionId;
            if (!ReferenceEquals(_turn, run))
            {
                return;
            }

            State = AgentChatState.Generating;
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, run.Cancellation.Token))
            {
                // Events of another session/turn, or arriving after this turn stopped being current, are discarded.
                if (!ReferenceEquals(_turn, run) || item.SessionId != sessionId || item.TurnId != run.TurnId)
                {
                    continue;
                }

                Apply(run, item);
            }
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_turn, run) && IsBusy)
            {
                Finish(run, AgentTurnOutcome.Cancelled);
            }
        }
        catch (AgentRuntimeException exception)
        {
            if (exception.Code == "SessionBusy" || exception.Code == "UnknownSession")
            {
                ForgetSession();
            }

            run.ErrorCode = SafeCode(exception.Code);
            if (ReferenceEquals(_turn, run))
            {
                Finish(run, AgentTurnOutcome.Failed);
            }
        }
        catch (Exception)
        {
            run.ErrorCode = "RuntimeFailure";
            if (ReferenceEquals(_turn, run))
            {
                Finish(run, AgentTurnOutcome.Failed);
            }
        }
        finally
        {
            if (ReferenceEquals(_turn, run))
            {
                if (IsBusy)
                {
                    // The stream ended without a terminal event: never report success.
                    Finish(run, AgentTurnOutcome.OutcomeUnknown);
                }

                _turn = null;
                CurrentTurnCompletion = null;
            }

            run.Cancellation.Dispose();
            OnStateChanged(State);
        }
    }

    private async Task<AgentSessionId> EnsureSessionAsync(
        IAgentRuntime runtime, string providerId, string? modelId, CancellationToken cancellationToken)
    {
        if (_sessionId is { } existing && _sessionProviderId == providerId && _sessionModelId == modelId)
        {
            return existing;
        }

        await CloseSessionAsync();
        var created = await runtime.StartSessionAsync(new AgentSessionOptions(providerId, modelId), cancellationToken);
        _sessionId = created;
        _sessionProviderId = providerId;
        _sessionModelId = modelId;
        return created;
    }

    private void Apply(TurnRun run, AgentEvent item)
    {
        switch (item.Kind)
        {
            case AgentEventKind.TaskStarted:
            case AgentEventKind.TaskProgress:
                RefreshRunningState(run);
                break;
            case AgentEventKind.MessageStarted when item.MessageId is { } id:
                GetOrAddMessage(run, id);
                break;
            case AgentEventKind.MessageDelta when item.MessageId is { } id && !string.IsNullOrEmpty(item.Text):
                // Streaming text is data only: appended, never interpreted as a command or an approval.
                GetOrAddMessage(run, id).Append(item.Text);
                break;
            case AgentEventKind.MessageCompleted when item.MessageId is { } id:
                if (run.Messages.TryGetValue(id, out var message))
                {
                    message.IsStreaming = false;
                }

                break;
            case AgentEventKind.ToolRequested when item.ToolCallId is { } callId:
                if (!run.Tools.ContainsKey(callId))
                {
                    var tool = new AgentToolCallItem(callId, item.ToolName, item.ToolDestination);
                    run.Tools.Add(callId, tool);
                    Items.Add(tool);
                }

                RefreshRunningState(run);
                break;
            case AgentEventKind.ToolStarted when item.ToolCallId is { } callId:
                if (run.Tools.TryGetValue(callId, out var started) && !started.IsTerminal)
                {
                    started.MarkStarted(_services.Clock.GetTimestamp());
                }

                break;
            case AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed when item.ToolCallId is { } callId:
                if (run.Tools.TryGetValue(callId, out var finished) && !finished.IsTerminal)
                {
                    finished.Complete(MapToolState(item), SafeCodeOrNull(item.ErrorCode), _services.Clock);
                }

                RefreshRunningState(run);
                break;
            case AgentEventKind.ApprovalRequested when item.ApprovalId is { } approvalId:
                RequestApproval(run, approvalId, item.ApprovalExpiresAtUtc);
                break;
            case AgentEventKind.ApprovalGranted or AgentEventKind.ApprovalDenied when item.ApprovalId is { } approvalId:
                if (run.Approvals.TryGetValue(approvalId, out var card))
                {
                    card.Approval.CloseByRuntime(item.Kind == AgentEventKind.ApprovalGranted
                        ? AgentApprovalOutcome.Granted : AgentApprovalOutcome.Denied, SafeCodeOrNull(item.ErrorCode));
                }

                RefreshRunningState(run);
                break;
            case AgentEventKind.AgentError:
                run.ErrorCode = SafeCode(item.ErrorCode);
                break;
            case AgentEventKind.TaskCompleted:
                Finish(run, item.Outcome ?? AgentTurnOutcome.OutcomeUnknown);
                break;
            case AgentEventKind.SessionCompleted:
                ForgetSession();
                break;
        }
    }

    private AgentChatMessageItem GetOrAddMessage(TurnRun run, AgentMessageId id)
    {
        if (!run.Messages.TryGetValue(id, out var message))
        {
            message = new AgentChatMessageItem(AgentChatRole.Agent, "", id);
            run.Messages.Add(id, message);
            Items.Add(message);
        }

        return message;
    }

    private static AgentToolCallState MapToolState(AgentEvent item) => item.Kind == AgentEventKind.ToolCompleted
        ? AgentToolCallState.Succeeded
        : item.ToolStatus switch
        {
            AgentToolResultStatus.Denied => AgentToolCallState.Denied,
            AgentToolResultStatus.Cancelled => AgentToolCallState.Cancelled,
            AgentToolResultStatus.OutcomeUnknown => AgentToolCallState.OutcomeUnknown,
            AgentToolResultStatus.Succeeded => AgentToolCallState.Succeeded,
            _ => AgentToolCallState.Failed,
        };

    private void RefreshRunningState(TurnRun run)
    {
        if (!ReferenceEquals(_turn, run) || State == AgentChatState.Cancelling)
        {
            return;
        }

        State = run.Approvals.Values.Any(static card => card.IsPending) ? AgentChatState.WaitingApproval
            : run.Tools.Values.Any(static tool => !tool.IsTerminal) ? AgentChatState.WaitingTool
            : AgentChatState.Generating;
    }

    private void RequestApproval(TurnRun run, AgentApprovalId approvalId, DateTimeOffset? runtimeExpiresAtUtc)
    {
        if (run.SessionId is not { } sessionId || run.Approvals.ContainsKey(approvalId))
        {
            return;
        }

        var approval = new AgentApprovalViewModel(_services.Runtime!, sessionId, run.TurnId, approvalId,
            _services.ApprovalDetails, _services.Clock, runtimeExpiresAtUtc);
        var card = new AgentApprovalCardItem(approval);
        approval.Settled += (_, state) =>
        {
            card.State = state;
            RefreshRunningState(run);
        };
        run.Approvals.Add(approvalId, card);
        Items.Add(card);
        RefreshRunningState(run);
        _ = approval.LoadAsync(run.Cancellation.Token);
        ApprovalRequested?.Invoke(this, approval);
    }

    /// <summary>Reopens a pending approval from its conversation card.</summary>
    [RelayCommand]
    private void OpenApproval(AgentApprovalCardItem? card)
    {
        if (card is { IsPending: true })
        {
            ApprovalRequested?.Invoke(this, card.Approval);
        }
    }

    private void Finish(TurnRun run, AgentTurnOutcome outcome)
    {
        foreach (var message in run.Messages.Values)
        {
            message.IsStreaming = false;
        }

        foreach (var card in run.Approvals.Values.Where(static card => card.IsPending))
        {
            card.Approval.CloseByRuntime(AgentApprovalOutcome.Denied, "ApprovalCancelled");
        }

        StatusDetail = run.ErrorCode;
        State = outcome switch
        {
            AgentTurnOutcome.Completed => AgentChatState.Completed,
            AgentTurnOutcome.Cancelled => AgentChatState.Cancelled,
            AgentTurnOutcome.TimedOut => AgentChatState.TimedOut,
            AgentTurnOutcome.Failed => AgentChatState.Failed,
            _ => AgentChatState.OutcomeUnknown,
        };
    }

    private bool CanCancelTurn() => _turn is not null && State is not AgentChatState.Cancelling && IsBusy;

    /// <summary>Cancels only this tab's turn. Anything already dispatched may have taken effect: no rollback is shown.</summary>
    [RelayCommand(CanExecute = nameof(CanCancelTurn))]
    private async Task CancelTurnAsync()
    {
        if (_turn is not { } run)
        {
            return;
        }

        State = AgentChatState.Cancelling;
        if (run.SessionId is not { } sessionId)
        {
            await run.Cancellation.CancelAsync();
            return;
        }

        try
        {
            await _services.Runtime!.CancelTurnAsync(sessionId, run.TurnId, CancellationToken.None);
        }
        catch (AgentRuntimeException exception) when (exception.Code == "UnknownTurn")
        {
            // Already finished: its terminal event decides the state.
        }
        catch (Exception)
        {
            // Interruption not confirmed: stop consuming; the runtime reports OutcomeUnknown if it cannot confirm.
            run.ErrorCode = "CancellationUnconfirmed";
            if (!run.Cancellation.IsCancellationRequested)
            {
                await run.Cancellation.CancelAsync();
            }
        }
    }

    private void ForgetSession()
    {
        _sessionId = null;
        _sessionProviderId = null;
        _sessionModelId = null;
    }

    private async Task CloseSessionAsync()
    {
        if (_sessionId is not { } sessionId || _services.Runtime is not { } runtime)
        {
            ForgetSession();
            return;
        }

        ForgetSession();
        try
        {
            await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
        }
        catch (Exception)
        {
            // Closing is best effort; the runtime disposes an adapter that does not confirm shutdown.
        }
    }

    private static string? SafeCodeOrNull(string? code) => code is null ? null : SafeCode(code);
}
