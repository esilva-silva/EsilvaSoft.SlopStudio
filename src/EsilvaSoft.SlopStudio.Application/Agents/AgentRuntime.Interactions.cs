using System.Diagnostics;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentRuntime
{
    private const string PermissionDeniedCode = "PermissionDenied";

    public async Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateSessionId(result.SessionId);
        if (!result.TurnId.IsValid || !result.ToolCallId.IsValid || !Enum.IsDefined(result.Status))
        {
            throw new AgentRuntimeException("InvalidToolResult", "Tool result identity is invalid.");
        }

        var session = FindSession(result.SessionId);
        var turn = FindActiveTurn(session, result.TurnId, "UnknownToolCall", "Tool call is unavailable or already answered.");
        ToolCallState tool;
        lock (turn.Gate)
        {
            // Runtime-dispatched calls are answered only by the registry path, never by an external submitter.
            if (turn.Finalizing || !turn.Tools.TryGetValue(result.ToolCallId, out var found) ||
                found.RuntimeDispatch || found.State != InteractionState.Pending)
            {
                throw new AgentRuntimeException("UnknownToolCall", "Tool call is unavailable or already answered.");
            }

            tool = found;
            tool.State = InteractionState.Submitting;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turn.Token, cancellationToken);
        // Before delivery nothing reached the provider, so a rejected or cancelled submission leaves the call
        // answerable (a forged result cannot consume the legitimate one). Once delivery starts it may be consumed.
        var delivering = false;
        try
        {
            if (!await ValidateWithAuthorityAsync(authority => authority.ValidateToolResultAsync(result, linked.Token))
                    .ConfigureAwait(false))
            {
                linked.Token.ThrowIfCancellationRequested();
                throw new AgentRuntimeException("UntrustedToolResult", "Tool result was not verified by the broker.");
            }

            linked.Token.ThrowIfCancellationRequested();
            var tooLarge = result.Data is { } data && data.Length > _options.MaxToolResultChars;
            var delivered = tooLarge
                ? result with { Status = AgentToolResultStatus.Failed, Data = null, ErrorCode = "ToolResultTooLarge", Truncated = false }
                : result;
            if (!PublishToolTerminal(turn, result.ToolCallId, delivered.Status,
                    tooLarge ? "ToolResultTooLarge" : SafeCode(delivered.ErrorCode)))
            {
                throw new AgentRuntimeException("UnknownToolCall", "Tool call is no longer active.");
            }

            delivering = true;
            // Never acquire a runtime lock while awaiting the provider: its stream may be waiting for this result.
            var delivery = turn.TryStartTracked(() => session.ProviderSession.SubmitToolResultAsync(delivered, linked.Token))
                ?? throw new AgentRuntimeException("UnknownToolCall", "Tool call is no longer active.");
            await delivery.ConfigureAwait(false);
            if (tooLarge)
            {
                // The provider was told the call failed, so the turn continues; the oversized data never left.
                throw new AgentRuntimeException("ToolResultTooLarge", "Tool result exceeds the runtime limit.");
            }
        }
        catch (AgentRuntimeException)
        {
            throw;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (delivering && !turn.Token.IsCancellationRequested)
            {
                turn.Fail("ToolResultDeliveryFailed");
            }

            throw;
        }
        catch (Exception)
        {
            // The provider may or may not have consumed it; the turn cannot continue on an uncertain state.
            turn.Fail("ToolResultDeliveryFailed");
            throw new AgentRuntimeException("ToolResultDeliveryFailed", "Tool result delivery could not be confirmed.");
        }
        finally
        {
            lock (turn.Gate)
            {
                // Consumed once delivery started, even when uncertain: replay could duplicate a provider-side action.
                tool.State = delivering ? InteractionState.Answered : InteractionState.Pending;
            }
        }
    }

    public async Task DecideApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ValidateSessionId(decision.SessionId);
        if (!decision.TurnId.IsValid || !decision.ApprovalId.IsValid || !Enum.IsDefined(decision.Outcome))
        {
            throw new AgentRuntimeException("InvalidApprovalDecision", "Approval identity is invalid.");
        }

        var session = FindSession(decision.SessionId);
        var turn = FindActiveTurn(session, decision.TurnId, "UnknownApproval", "Approval is unavailable or already decided.");
        ApprovalState approval;
        lock (turn.Gate)
        {
            if (turn.Finalizing || !turn.Approvals.TryGetValue(decision.ApprovalId, out var found) ||
                found.State != InteractionState.Pending)
            {
                throw new AgentRuntimeException("UnknownApproval", "Approval is unavailable or already decided.");
            }

            approval = found;
            approval.State = InteractionState.Submitting;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turn.Token, cancellationToken);
        var consumed = false;
        try
        {
            // The decision must come from the trusted approver; provider or model output can never reach this path.
            if (!await ValidateWithAuthorityAsync(authority => authority.ValidateApprovalDecisionAsync(decision, linked.Token))
                    .ConfigureAwait(false))
            {
                linked.Token.ThrowIfCancellationRequested();
                throw new AgentRuntimeException("UntrustedApprovalDecision", "Approval was not verified by the approver.");
            }

            linked.Token.ThrowIfCancellationRequested();
            consumed = true;
            if (IsExpired(approval))
            {
                var denial = turn.TryStartTracked(() => CompleteApprovalAsync(session, turn, decision.ApprovalId,
                    AgentApprovalOutcome.Denied, "ApprovalExpired", deliver: true));
                if (denial is not null)
                {
                    await denial.ConfigureAwait(false);
                }

                throw new AgentRuntimeException("ApprovalExpired", "Approval expired.");
            }

            if (!PublishApprovalTerminal(turn, decision.ApprovalId, decision.Outcome, null))
            {
                throw new AgentRuntimeException("UnknownApproval", "Approval is no longer active.");
            }

            var delivery = turn.TryStartTracked(() => session.ProviderSession.SubmitApprovalAsync(decision, linked.Token))
                ?? throw new AgentRuntimeException("UnknownApproval", "Approval is no longer active.");
            await delivery.ConfigureAwait(false);
        }
        catch (AgentRuntimeException)
        {
            throw;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (consumed && !turn.Token.IsCancellationRequested)
            {
                turn.Fail("ApprovalDeliveryFailed");
            }

            throw;
        }
        catch (Exception)
        {
            turn.Fail("ApprovalDeliveryFailed");
            throw new AgentRuntimeException("ApprovalDeliveryFailed", "Approval delivery could not be confirmed.");
        }
        finally
        {
            bool expireNow;
            lock (turn.Gate)
            {
                // A rejected or cancelled decision reached nobody: the approval stays answerable by the real approver,
                // unless it expired meanwhile, in which case it is denied now (the timer skipped it while validating).
                expireNow = !consumed && IsExpired(approval) && !turn.Finalizing;
                approval.State = consumed || expireNow ? InteractionState.Answered : InteractionState.Pending;
            }

            if (expireNow)
            {
                _ = turn.TryStartTracked(() => CompleteApprovalAsync(session, turn, decision.ApprovalId,
                    AgentApprovalOutcome.Denied, "ApprovalExpired", deliver: true));
            }
        }
    }

    /// <summary>Monotonic check: wall-clock changes cannot extend or shorten an approval window.</summary>
    private bool IsExpired(ApprovalState approval) =>
        Volatile.Read(ref approval.ExpiredFlag) != 0 ||
        Stopwatch.GetElapsedTime(approval.RequestedAtTimestamp) >= _options.ApprovalTimeout;

    private static TurnState FindActiveTurn(SessionState session, AgentTurnId turnId, string code, string message)
    {
        lock (session.Gate)
        {
            if (session.Closed || session.ActiveTurn is not { } turn || turn.TurnId != turnId)
            {
                throw new AgentRuntimeException(code, message);
            }

            return turn;
        }
    }

    private async Task<bool> HandleToolRequestAsync(SessionState session, TurnState turn, AgentProviderEvent item)
    {
        if (item.ToolCallId is not { } callId)
        {
            turn.Fail("ProviderProtocolViolation");
            return false;
        }

        var runtimeDispatch = _toolRegistry is not null;
        lock (turn.Gate)
        {
            if (turn.Tools.ContainsKey(callId))
            {
                return true; // Duplicate request from the adapter: discarded, never dispatched twice.
            }
        }

        if (CountTools(turn) >= _options.MaxToolCallsPerTurn)
        {
            turn.Fail("ToolCallLimitExceeded");
            return false;
        }

        string? descriptorName = null;
        if (runtimeDispatch)
        {
            if (string.IsNullOrWhiteSpace(item.ToolName) || item.ToolName.Length > 96 ||
                (item.ArgumentsJson?.Length ?? 0) > _options.MaxToolArgumentsChars)
            {
                turn.Fail("InvalidToolRequest");
                return false;
            }

            try
            {
                // Only a registry-known canonical name is ever published; raw model text is not echoed.
                descriptorName = _toolRegistry!.FindDescriptor(item.ToolName)?.Name;
            }
            catch (Exception)
            {
                descriptorName = null;
            }
        }
        else if (!await ValidateWithAuthorityAsync(authority =>
                     authority.ValidateToolRequestAsync(turn.SessionId, turn.TurnId, callId, turn.Token)).ConfigureAwait(false))
        {
            turn.Fail("UntrustedToolRequest");
            return false;
        }

        var tool = new ToolCallState(descriptorName, runtimeDispatch)
        {
            State = runtimeDispatch ? InteractionState.Dispatching : InteractionState.Pending,
        };
        lock (turn.Gate)
        {
            if (turn.Finalizing)
            {
                return false;
            }

            if (!turn.Tools.TryAdd(callId, tool))
            {
                return true;
            }

            // Registration, answerable state and publication are one step: a broker that learns the call ID out of
            // band cannot answer before the request exists, and its terminal can never precede the request event.
            // This control publication is bounded by MaxToolCallsPerTurn instead of consumer back-pressure.
            turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, AgentEventKind.ToolRequested,
                toolCallId: callId, toolName: descriptorName));
        }

        if (runtimeDispatch)
        {
            var name = item.ToolName!;
            var arguments = item.ArgumentsJson;
            // Dispatch runs off the stream: the pump keeps reading while the registry executes.
            _ = turn.TryStartTracked(() => DispatchToolAsync(session, turn, callId, tool, name, arguments));
        }

        return true;
    }

    private static int CountTools(TurnState turn)
    {
        lock (turn.Gate)
        {
            return turn.Tools.Count;
        }
    }

    private async Task DispatchToolAsync(
        SessionState session, TurnState turn, AgentToolCallId callId, ToolCallState tool, string name, string? arguments)
    {
        AgentToolResult result;
        var started = false;
        var sessionSlot = false;
        var globalSlot = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(turn.Token);
        deadline.CancelAfter(_options.ToolTimeout);
        try
        {
            if (tool.DescriptorName is null)
            {
                result = ToolFailure(turn, callId, AgentToolResultStatus.Failed, "UnknownTool");
            }
            else
            {
                await session.ToolSlots.WaitAsync(deadline.Token).ConfigureAwait(false);
                sessionSlot = true;
                await _globalToolSlots.WaitAsync(deadline.Token).ConfigureAwait(false);
                globalSlot = true;
                var binding = await _toolBindings!.ResolveAsync(turn.SessionId, turn.TurnId, session.ProviderId,
                    tool.DescriptorName, deadline.Token).ConfigureAwait(false);
                var context = binding is null ? null : CreateInvocationContext(session, turn, binding);
                if (binding is null || context is null)
                {
                    result = ToolFailure(turn, callId, AgentToolResultStatus.Denied, PermissionDeniedCode);
                }
                else if (!MarkToolStarted(turn, callId))
                {
                    return;
                }
                else
                {
                    started = true;
                    var invocation = await _toolRegistry!.InvokeAsync(binding.Principal, context, binding.Destination,
                        binding.OutputDataScope, tool.DescriptorName, arguments, deadline.Token).ConfigureAwait(false);
                    result = MapRegistryResult(turn, callId, invocation);
                    if (result.Status == AgentToolResultStatus.Succeeded &&
                        !await IsReleaseStillAuthorizedAsync(session, turn, tool.DescriptorName, binding, deadline.Token)
                            .ConfigureAwait(false))
                    {
                        // Revoked principal, changed policy or rebound destination: the data is not released.
                        result = ToolFailure(turn, callId, AgentToolResultStatus.Denied, PermissionDeniedCode);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // Once dispatched, a cancelled call may still have run: report OutcomeUnknown, never a rollback.
            result = started
                ? ToolFailure(turn, callId, AgentToolResultStatus.OutcomeUnknown, "ToolOutcomeUnknown")
                : turn.Token.IsCancellationRequested
                    ? ToolFailure(turn, callId, AgentToolResultStatus.Cancelled, "ToolCancelled")
                    : ToolFailure(turn, callId, AgentToolResultStatus.Failed, "ToolDeadlineExceeded");
        }
        catch (Exception)
        {
            result = started
                ? ToolFailure(turn, callId, AgentToolResultStatus.OutcomeUnknown, "ToolOutcomeUnknown")
                : ToolFailure(turn, callId, AgentToolResultStatus.Failed, "ToolDispatchFailed");
        }
        finally
        {
            if (globalSlot)
            {
                _globalToolSlots.Release();
            }

            if (sessionSlot)
            {
                session.ToolSlots.Release();
            }
        }

        if (!PublishToolTerminal(turn, callId, result.Status, result.ErrorCode) || turn.Token.IsCancellationRequested)
        {
            return; // Late result of a finished or cancelled turn: discarded, not delivered.
        }

        try
        {
            await session.ProviderSession.SubmitToolResultAsync(result, turn.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (turn.Token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            turn.Fail("ToolResultDeliveryFailed");
        }
    }

    private static AgentInvocationContext? CreateInvocationContext(SessionState session, TurnState turn, AgentToolBinding binding)
    {
        if (!MatchesSession(session, binding))
        {
            return null;
        }

        try
        {
            return new AgentInvocationContext(session.ProviderId, null,
                Guid.ParseExact(turn.SessionId.Value, "N"), Guid.ParseExact(turn.TurnId.Value, "N"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The destination comes from the provider declaration: an external provider only ever receives
    /// <c>ProviderExternal(its id)</c>; <c>Local</c> is valid only for a provider declared local. Any divergence denies.
    /// </summary>
    private static bool MatchesSession(SessionState session, AgentToolBinding binding) =>
        binding.Principal is not null && binding.Destination is not null &&
        Enum.IsDefined(binding.OutputDataScope) && binding.Destination == session.OutputDestination;

    /// <summary>Revalidation right before publication to the provider (after the registry, which may have blocked).</summary>
    private async Task<bool> IsReleaseStillAuthorizedAsync(
        SessionState session, TurnState turn, string toolName, AgentToolBinding original, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _principalAuthority!.IsCurrentAsync(original.Principal, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var current = await _toolBindings!.ResolveAsync(turn.SessionId, turn.TurnId, session.ProviderId, toolName,
                cancellationToken).ConfigureAwait(false);
            return current is not null && MatchesSession(session, current) &&
                current.Principal.Id == original.Principal.Id &&
                current.Principal.PolicyRevision == original.Principal.PolicyRevision &&
                current.OutputDataScope == original.OutputDataScope;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private AgentToolResult MapRegistryResult(TurnState turn, AgentToolCallId callId, AgentToolInvocationResult invocation)
    {
        if (invocation.Succeeded)
        {
            return invocation.StructuredContentJson is { } data && data.Length <= _options.MaxToolResultChars
                ? new AgentToolResult(turn.SessionId, turn.TurnId, callId, AgentToolResultStatus.Succeeded, data)
                : ToolFailure(turn, callId, AgentToolResultStatus.Failed, "ToolResultTooLarge");
        }

        var code = SafeCode(invocation.ErrorCode) ?? "ToolFailed";
        return ToolFailure(turn, callId,
            code == PermissionDeniedCode ? AgentToolResultStatus.Denied : AgentToolResultStatus.Failed, code);
    }

    private static AgentToolResult ToolFailure(
        TurnState turn, AgentToolCallId callId, AgentToolResultStatus status, string code) =>
        new(turn.SessionId, turn.TurnId, callId, status, ErrorCode: code);

    /// <summary>Only short ASCII identifiers leave as codes; anything else could carry untrusted text.</summary>
    private static string? SafeCode(string? code) =>
        code is { Length: > 0 and <= 64 } && code.All(char.IsAsciiLetterOrDigit) ? code : code is null ? null : "ToolFailed";

    private static bool MarkToolStarted(TurnState turn, AgentToolCallId callId)
    {
        lock (turn.Gate)
        {
            if (turn.Finalized || !turn.Tools.TryGetValue(callId, out var tool) || tool.Terminal)
            {
                return false;
            }

            tool.Started = true;
            return turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, AgentEventKind.ToolStarted,
                toolCallId: callId, toolName: tool.DescriptorName));
        }
    }

    /// <summary>Publishes the single terminal event of a call; false when it already has one or the turn ended.</summary>
    private static bool PublishToolTerminal(TurnState turn, AgentToolCallId callId, AgentToolResultStatus status, string? errorCode)
    {
        lock (turn.Gate)
        {
            if (turn.Finalized || !turn.Tools.TryGetValue(callId, out var tool) || tool.Terminal)
            {
                return false;
            }

            tool.Terminal = true;
            turn.ToolOutcomeUnknown |= status == AgentToolResultStatus.OutcomeUnknown;
            var kind = status == AgentToolResultStatus.Succeeded ? AgentEventKind.ToolCompleted : AgentEventKind.ToolFailed;
            return turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, kind, errorCode: errorCode,
                toolCallId: callId, toolName: tool.DescriptorName, toolStatus: status));
        }
    }

    private async Task<bool> HandleApprovalRequestAsync(SessionState session, TurnState turn, AgentProviderEvent item)
    {
        if (item.ApprovalId is not { } approvalId)
        {
            turn.Fail("ProviderProtocolViolation");
            return false;
        }

        bool limitExceeded;
        lock (turn.Gate)
        {
            if (turn.Approvals.ContainsKey(approvalId))
            {
                return true; // Duplicate request: discarded.
            }

            limitExceeded = turn.Approvals.Count >= _options.MaxApprovalsPerTurn;
        }

        if (limitExceeded)
        {
            turn.Fail("ApprovalLimitExceeded");
            return false;
        }

        if (!await ValidateWithAuthorityAsync(authority =>
                authority.ValidateApprovalRequestAsync(turn.SessionId, turn.TurnId, approvalId, turn.Token)).ConfigureAwait(false))
        {
            turn.Fail("UntrustedApprovalRequest");
            return false;
        }

        var approval = new ApprovalState { State = InteractionState.Pending, RequestedAtTimestamp = Stopwatch.GetTimestamp() };
        lock (turn.Gate)
        {
            if (turn.Finalizing)
            {
                return false;
            }

            if (!turn.Approvals.TryAdd(approvalId, approval))
            {
                return true;
            }

            // Same atomic registration as tools; WaitingApproval holds no runtime lock, connection or tool slot.
            turn.Queue.TryEnqueueControl(sequence =>
            {
                var created = turn.Create(sequence, AgentEventKind.ApprovalRequested, approvalId: approvalId);
                // Display deadline from the runtime's own timeout, anchored on the monotonic start so it never shows
                // more time than enforcement allows; enforcement stays on the monotonic timestamp.
                return created with
                {
                    ApprovalExpiresAtUtc = created.TimestampUtc -
                        Stopwatch.GetElapsedTime(approval.RequestedAtTimestamp) + _options.ApprovalTimeout,
                };
            });
        }

        _ = turn.TryStartTracked(() => ExpireApprovalAsync(session, turn, approvalId, approval));
        return true;
    }

    private async Task ExpireApprovalAsync(SessionState session, TurnState turn, AgentApprovalId approvalId, ApprovalState approval)
    {
        try
        {
            await Task.Delay(_options.ApprovalTimeout, turn.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (turn.Gate)
        {
            Volatile.Write(ref approval.ExpiredFlag, 1);
            if (turn.Finalizing || approval.State != InteractionState.Pending)
            {
                return; // A decision under validation re-checks expiry when it is rejected.
            }

            approval.State = InteractionState.Answered;
        }

        await CompleteApprovalAsync(session, turn, approvalId, AgentApprovalOutcome.Denied, "ApprovalExpired", deliver: true)
            .ConfigureAwait(false);
    }

    /// <summary>Denial generated by the runtime (expiry). It fails closed and needs no approver.</summary>
    private static async Task CompleteApprovalAsync(
        SessionState session, TurnState turn, AgentApprovalId approvalId, AgentApprovalOutcome outcome, string? errorCode,
        bool deliver)
    {
        if (!PublishApprovalTerminal(turn, approvalId, outcome, errorCode) || !deliver || turn.Token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await session.ProviderSession.SubmitApprovalAsync(
                new AgentApprovalDecision(turn.SessionId, turn.TurnId, approvalId, outcome), turn.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (turn.Token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            turn.Fail("ApprovalDeliveryFailed");
        }
    }

    private static bool PublishApprovalTerminal(
        TurnState turn, AgentApprovalId approvalId, AgentApprovalOutcome outcome, string? errorCode)
    {
        lock (turn.Gate)
        {
            if (turn.Finalized || !turn.Approvals.TryGetValue(approvalId, out var approval) || approval.Terminal)
            {
                return false;
            }

            approval.Terminal = true;
            var kind = outcome == AgentApprovalOutcome.Granted ? AgentEventKind.ApprovalGranted : AgentEventKind.ApprovalDenied;
            return turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, kind, errorCode: errorCode,
                approvalId: approvalId));
        }
    }

    private async Task<bool> ValidateWithAuthorityAsync(Func<IAgentInteractionAuthority, Task<bool>> validate)
    {
        if (_interactionAuthority is null)
        {
            return false;
        }

        try
        {
            return await validate(_interactionAuthority).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
