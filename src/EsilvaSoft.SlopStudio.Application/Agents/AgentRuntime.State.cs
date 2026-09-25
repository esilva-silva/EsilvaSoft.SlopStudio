using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentRuntime
{
    /// <summary>First cause wins; later requests only make sure the turn token is cancelled.</summary>
    private enum TurnCancelReason
    {
        None = 0,
        User,
        Consumer,
        ConsumerAbandoned,
        SessionClosed,
        ConsumerUnavailable,
        TimedOut,
        Failed,
        Finished,
    }

    private enum InteractionState
    {
        Announcing,
        Pending,
        Dispatching,
        Submitting,
        Answered,
    }

    /// <summary>Lock order is always session gate, then turn gate, then the queue's private lock.</summary>
    private sealed class SessionState(
        IAgentSession providerSession, string providerId, AgentOutputDestination outputDestination, int maxConcurrentTools)
    {
        private long _sequence;

        public object Gate { get; } = new();

        public IAgentSession ProviderSession { get; } = providerSession;

        public string ProviderId { get; } = providerId;

        /// <summary>Destination derived from the provider declaration; bindings must match it exactly.</summary>
        public AgentOutputDestination OutputDestination { get; } = outputDestination;

        public CancellationTokenSource Lifetime { get; } = new();

        public SemaphoreSlim ToolSlots { get; } = new(maxConcurrentTools, maxConcurrentTools);

        public HashSet<AgentTurnId> TurnIds { get; } = [];

        public TurnState? ActiveTurn { get; set; }

        public Task TurnDrained { get; set; } = Task.CompletedTask;

        public Task? LifetimeCancellationTask { get; set; }

        public Task? ProviderCancelTask { get; set; }

        public Task<bool>? InterruptTask { get; set; }

        public Task<bool>? ShutdownTask { get; set; }

        public bool Closed { get; set; }

        public long NextSequence() => Interlocked.Increment(ref _sequence);
    }

    private sealed class MessageState(AgentMessageId id)
    {
        public AgentMessageId Id { get; } = id;

        public bool Open { get; set; } = true;
    }

    private sealed class ToolCallState(string? descriptorName, bool runtimeDispatch)
    {
        public string? DescriptorName { get; } = descriptorName;

        public bool RuntimeDispatch { get; } = runtimeDispatch;

        public InteractionState State { get; set; } = InteractionState.Announcing;

        public bool Started { get; set; }

        public bool Terminal { get; set; }
    }

    private sealed class ApprovalState
    {
        public InteractionState State { get; set; } = InteractionState.Announcing;

        /// <summary>Monotonic timestamp (<see cref="System.Diagnostics.Stopwatch"/>), immune to wall-clock changes.</summary>
        public long RequestedAtTimestamp { get; set; }

        /// <summary>Set to 1 by the expiry timer even while a decision is being validated.</summary>
        public int ExpiredFlag;

        public bool Terminal { get; set; }
    }

    /// <summary>
    /// One turn. The CTS is owned by this turn only (never shared across sessions or tabs) and is not linked: external
    /// tokens are attached through registrations that are disposed when the turn finishes.
    /// </summary>
    private sealed class TurnState
    {
        private readonly object _cancelGate = new();
        private CancellationTokenRegistration _consumerRegistration;
        private CancellationTokenRegistration _lifetimeRegistration;
        private Task? _cancelTask;
        private int _reason;
        private string? _failureCode;

        public TurnState(AgentSessionId sessionId, SessionState session, AgentTurnRequest request, AgentRuntimeOptions options)
        {
            SessionId = sessionId;
            Session = session;
            Request = request;
            TurnId = request.TurnId;
            Token = Cts.Token;
            Queue = new AgentRuntimeEventQueue(session.NextSequence, options.MaxQueuedEvents, options.MaxQueuedBytes,
                options.MaxDeltaChars);
        }

        public AgentSessionId SessionId { get; }

        public SessionState Session { get; }

        public AgentTurnRequest Request { get; }

        public AgentTurnId TurnId { get; }

        public Guid CorrelationId { get; } = Guid.NewGuid();

        public CancellationTokenSource Cts { get; } = new();

        public CancellationToken Token { get; }

        public AgentRuntimeEventQueue Queue { get; }

        public object Gate { get; } = new();

        public Dictionary<string, MessageState> Messages { get; } = new(StringComparer.Ordinal);

        public Dictionary<AgentToolCallId, ToolCallState> Tools { get; } = [];

        public Dictionary<AgentApprovalId, ApprovalState> Approvals { get; } = [];

        public List<Task> Background { get; } = [];

        /// <summary>Set under <see cref="Gate"/> when the pump stops; no new work or delivery may start.</summary>
        public bool Finalizing { get; set; }

        /// <summary>Set under <see cref="Gate"/> with the terminal event; every later event is discarded.</summary>
        public bool Finalized { get; set; }

        public bool ToolOutcomeUnknown { get; set; }

        public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PumpTask { get; set; } = Task.CompletedTask;

        public TurnCancelReason Reason => (TurnCancelReason)Volatile.Read(ref _reason);

        public string? FailureCode => Volatile.Read(ref _failureCode);

        public AgentEvent Create(
            long sequence, AgentEventKind kind, string? text = null, AgentTurnOutcome? outcome = null,
            string? errorCode = null, AgentToolCallId? toolCallId = null, AgentApprovalId? approvalId = null,
            AgentMessageId? messageId = null, string? toolName = null, AgentToolResultStatus? toolStatus = null) =>
            new(1, Guid.NewGuid(), SessionId, TurnId, sequence, DateTimeOffset.UtcNow, CorrelationId, kind, text,
                outcome, errorCode, toolCallId, approvalId, messageId, toolName, toolStatus)
            {
                // Sanitized kind only, from the session destination (provider IsLocal); never from provider output.
                ToolDestination = toolCallId is null ? null
                    : Session.OutputDestination.IsExternal ? AgentDataDestinationKind.External : AgentDataDestinationKind.Local,
            };

        public void AttachCancellation(CancellationToken consumer, CancellationToken lifetime)
        {
            _consumerRegistration = consumer.Register(
                static state => ((TurnState)state!).RequestCancel(TurnCancelReason.Consumer), this);
            _lifetimeRegistration = lifetime.Register(
                static state => ((TurnState)state!).RequestCancel(TurnCancelReason.SessionClosed), this);
        }

        public void DetachCancellation()
        {
            _consumerRegistration.Dispose();
            _lifetimeRegistration.Dispose();
        }

        public void RequestCancel(TurnCancelReason reason)
        {
            Interlocked.CompareExchange(ref _reason, (int)reason, (int)TurnCancelReason.None);
            lock (_cancelGate)
            {
                // CancelAsync runs callbacks off the caller, so no caller lock or stream continuation runs inline.
                _cancelTask ??= Cts.CancelAsync();
            }
        }

        public void SetFailure(string code) => Interlocked.CompareExchange(ref _failureCode, code, null);

        public void Fail(string code)
        {
            SetFailure(code);
            RequestCancel(TurnCancelReason.Failed);
        }

        /// <summary>Starts <paramref name="action"/> only if the turn still accepts work, and tracks it for drain.</summary>
        public Task? TryStartTracked(Func<Task> action)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = RunAfterAsync(gate.Task, action);
            lock (Gate)
            {
                if (Finalizing)
                {
                    gate.SetCanceled();
                    return null;
                }

                Background.Add(task);
            }

            gate.SetResult();
            return task;
        }

        private static async Task RunAfterAsync(Task gate, Func<Task> action)
        {
            await gate.ConfigureAwait(false);
            await action().ConfigureAwait(false);
        }
    }
}
