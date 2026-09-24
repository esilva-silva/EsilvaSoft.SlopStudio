using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Owns independent provider sessions and publishes a normalized stream per turn.</summary>
public sealed class AgentRuntime : IAgentRuntime, IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private readonly Dictionary<string, IAgentProvider> _providers;
    private readonly ConcurrentDictionary<AgentSessionId, SessionState> _sessions = new();
    private int _disposed;

    public AgentRuntime(IEnumerable<IAgentProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var byId = new Dictionary<string, IAgentProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (provider is null || string.IsNullOrWhiteSpace(provider.ProviderId))
            {
                throw new ArgumentException("Provider ID is required.", nameof(providers));
            }

            if (!byId.TryAdd(provider.ProviderId, provider))
            {
                throw new AgentRuntimeException("DuplicateProviderId", "Provider ID is duplicated.");
            }
        }

        _providers = byId;
    }

    public async Task<AgentSessionId> StartSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProviderId) || !_providers.TryGetValue(options.ProviderId, out var provider))
        {
            throw new AgentRuntimeException("UnknownProvider", "Provider is unavailable.");
        }

        IAgentSession providerSession;
        try
        {
            providerSession = await provider.CreateSessionAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AgentRuntimeException("ProviderUnavailable", "Provider session could not be started.");
        }

        if (providerSession is null)
        {
            throw new AgentRuntimeException("ProviderUnavailable", "Provider session could not be started.");
        }

        var id = AgentSessionId.New();
        if (!_sessions.TryAdd(id, new SessionState(providerSession)))
        {
            await providerSession.DisposeAsync().ConfigureAwait(false);
            throw new AgentRuntimeException("DuplicateSessionId", "Session ID is duplicated.");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            await CloseSessionAsync(id, CancellationToken.None).ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(AgentRuntime));
        }

        return id;
    }

    public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentSessionId sessionId,
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        if (!request.TurnId.IsValid || string.IsNullOrWhiteSpace(request.TabId) || request.UserMessage is null || request.DocumentVersion < 0)
        {
            throw new AgentRuntimeException("InvalidTurn", "Turn request is invalid.");
        }

        var session = FindSession(sessionId);
        CancellationTokenSource turnCts;
        TaskCompletionSource turnDrained;
        lock (session.Gate)
        {
            if (session.Closed)
            {
                throw new AgentRuntimeException("UnknownSession", "Session is unavailable.");
            }

            if (session.TurnIds.Contains(request.TurnId))
            {
                throw new AgentRuntimeException("DuplicateTurnId", "Turn ID is duplicated.");
            }

            if (session.ActiveTurn is not null)
            {
                throw new AgentRuntimeException("SessionBusy", "Session already has an active turn.");
            }

            session.TurnIds.Add(request.TurnId);

            turnCts = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token, cancellationToken);
            turnDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.ActiveTurn = request.TurnId;
            session.ActiveCancellation = turnCts;
            session.TurnDrained = turnDrained;
            session.InterruptTask = null;
            session.ProviderCancelTask = null;
            session.CancellationTask = null;
        }

        var correlationId = Guid.NewGuid();
        AgentEvent Create(AgentEventKind kind, string? value = null, AgentTurnOutcome? outcome = null, string? errorCode = null) =>
            new(1, Guid.NewGuid(), sessionId, request.TurnId, Interlocked.Increment(ref session.Sequence),
                DateTimeOffset.UtcNow, correlationId, kind, value, outcome, errorCode);

        IAsyncEnumerator<AgentProviderEvent>? enumerator = null;
        Task<bool>? moveTask = null;
        Task<bool>? drainTask = null;
        var outcome = AgentTurnOutcome.Completed;
        var errorCode = "ProviderFailure";
        var messageOpen = false;
        var messageCompleted = false;
        try
        {
            yield return Create(AgentEventKind.TaskStarted);
            try
            {
                enumerator = session.ProviderSession.RunTurnAsync(request, turnCts.Token).GetAsyncEnumerator(turnCts.Token);
            }
            catch (OperationCanceledException) when (turnCts.IsCancellationRequested)
            {
                outcome = AgentTurnOutcome.Cancelled;
            }
            catch (Exception)
            {
                outcome = AgentTurnOutcome.Failed;
            }

            while (enumerator is not null)
            {
                AgentProviderEvent? providerEvent;
                bool hasNext;
                try
                {
                    moveTask = enumerator.MoveNextAsync().AsTask();
                    hasNext = await moveTask.WaitAsync(turnCts.Token).ConfigureAwait(false);
                    moveTask = null;
                    providerEvent = hasNext ? enumerator.Current : null;
                }
                catch (OperationCanceledException) when (turnCts.IsCancellationRequested)
                {
                    outcome = AgentTurnOutcome.Cancelled;
                    break;
                }
                catch (Exception)
                {
                    outcome = AgentTurnOutcome.Failed;
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                if (turnCts.IsCancellationRequested)
                {
                    outcome = AgentTurnOutcome.Cancelled;
                    break;
                }

                if (providerEvent is null)
                {
                    continue;
                }

                switch (providerEvent.Kind)
                {
                    case AgentEventKind.MessageStarted when !messageOpen && !messageCompleted:
                        messageOpen = true;
                        yield return Create(AgentEventKind.MessageStarted);
                        break;
                    case AgentEventKind.MessageDelta when messageOpen && !messageCompleted:
                        yield return Create(AgentEventKind.MessageDelta, providerEvent.Text);
                        break;
                    case AgentEventKind.MessageCompleted when messageOpen && !messageCompleted:
                        messageOpen = false;
                        messageCompleted = true;
                        yield return Create(AgentEventKind.MessageCompleted);
                        break;
                    case AgentEventKind.MessageStarted or AgentEventKind.MessageDelta or AgentEventKind.MessageCompleted:
                        // Duplicate or late message events cannot reopen completed content.
                        break;
                    case AgentEventKind.TaskCompleted:
                        // The runtime alone publishes the turn terminal.
                        break;
                    default:
                        // Tool and approval events require a trusted broker principal that this runtime does not own yet.
                        outcome = AgentTurnOutcome.Failed;
                        errorCode = "ProviderProtocolViolation";
                        break;
                }

                if (outcome == AgentTurnOutcome.Failed)
                {
                    break;
                }
            }

            var interrupted = turnCts.IsCancellationRequested;
            var interruptTask = interrupted ? InterruptProviderAsync(session, request.TurnId) : null;
            drainTask = Task.Run(() => DrainTurnAsync(session, request.TurnId, turnCts, turnDrained, enumerator, moveTask));
            try
            {
                var drained = interruptTask is null
                    ? await drainTask.WaitAsync(StopTimeout, CancellationToken.None).ConfigureAwait(false)
                    : (await Task.WhenAll(drainTask, interruptTask).WaitAsync(StopTimeout, CancellationToken.None).ConfigureAwait(false))
                        .All(result => result);
                if (!drained)
                {
                    outcome = AgentTurnOutcome.OutcomeUnknown;
                }
                else if (interrupted)
                {
                    outcome = AgentTurnOutcome.Cancelled;
                }
            }
            catch (TimeoutException)
            {
                outcome = AgentTurnOutcome.OutcomeUnknown;
            }

            if (outcome == AgentTurnOutcome.OutcomeUnknown)
            {
                _ = BeginCloseSession(sessionId, session);
            }

            if (messageOpen)
            {
                yield return Create(AgentEventKind.MessageCompleted);
            }

            if (outcome is AgentTurnOutcome.Failed or AgentTurnOutcome.OutcomeUnknown)
            {
                yield return Create(AgentEventKind.AgentError,
                    errorCode: outcome == AgentTurnOutcome.OutcomeUnknown ? "InterruptionUnconfirmed" : errorCode);
            }

            yield return Create(AgentEventKind.TaskCompleted, outcome: outcome);
        }
        finally
        {
            if (drainTask is null)
            {
                drainTask = Task.Run(() => DrainTurnAsync(session, request.TurnId, turnCts, turnDrained, enumerator, moveTask));
                try
                {
                    await drainTask.WaitAsync(StopTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    _ = BeginCloseSession(sessionId, session);
                }
            }
        }
    }

    public async Task CancelTurnAsync(AgentSessionId sessionId, AgentTurnId turnId, CancellationToken cancellationToken)
    {
        ValidateSessionId(sessionId);
        if (!turnId.IsValid)
        {
            throw new AgentRuntimeException("InvalidTurnId", "Turn ID is invalid.");
        }

        var session = FindSession(sessionId);
        lock (session.Gate)
        {
            if (session.ActiveTurn != turnId || session.ActiveCancellation is null)
            {
                throw new AgentRuntimeException("UnknownTurn", "Turn is not active in this session.");
            }

            session.CancellationTask ??= session.ActiveCancellation.CancelAsync();
        }

        var confirmed = await InterruptProviderAsync(session, turnId).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!confirmed)
        {
            _ = BeginCloseSession(sessionId, session);
            throw new AgentRuntimeException("CancellationUnconfirmed", "Provider interruption could not be confirmed.");
        }
    }

    public async Task CloseSessionAsync(AgentSessionId sessionId, CancellationToken cancellationToken)
    {
        ValidateSessionId(sessionId);
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new AgentRuntimeException("UnknownSession", "Session is unavailable.");
        }

        var shutdown = BeginCloseSession(sessionId, session);
        bool disposed;
        try
        {
            disposed = await shutdown.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new AgentRuntimeException("SessionShutdownUnconfirmed", "Provider session shutdown could not be confirmed.");
        }

        if (!disposed)
        {
            throw new AgentRuntimeException("SessionShutdownUnconfirmed", "Provider session shutdown could not be confirmed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var id in _sessions.Keys)
        {
            try
            {
                await CloseSessionAsync(id, CancellationToken.None).ConfigureAwait(false);
            }
            catch (AgentRuntimeException exception) when (exception.Code == "UnknownSession")
            {
                // Concurrent close already removed it.
            }
        }
    }

    private SessionState FindSession(AgentSessionId id) =>
        _sessions.TryGetValue(id, out var session)
            ? session
            : throw new AgentRuntimeException("UnknownSession", "Session is unavailable.");

    private static void ValidateSessionId(AgentSessionId id)
    {
        if (!id.IsValid)
        {
            throw new AgentRuntimeException("InvalidSessionId", "Session ID is invalid.");
        }
    }

    private Task<bool> BeginCloseSession(AgentSessionId id, SessionState session)
    {
        lock (session.Gate)
        {
            if (session.ShutdownTask is not null)
            {
                return session.ShutdownTask;
            }

            session.Closed = true;
            _sessions.TryRemove(id, out _);
            session.LifetimeCancellationTask ??= session.Lifetime.CancelAsync();
            if (session.ActiveTurn is { } turnId)
            {
                session.InterruptTask ??= StartInterruptProvider(session, turnId);
            }

            session.ShutdownTask = Task.Run(() => ShutdownSessionAsync(session));
            return session.ShutdownTask;
        }
    }

    private static async Task<bool> ShutdownSessionAsync(SessionState session)
    {
        Task drained;
        Task? cancellation;
        Task? lifetimeCancellation;
        Task? providerCancel;
        lock (session.Gate)
        {
            drained = session.TurnDrained?.Task ?? Task.CompletedTask;
            cancellation = session.CancellationTask;
            lifetimeCancellation = session.LifetimeCancellationTask;
            providerCancel = session.ProviderCancelTask;
        }

        var lifetimeDisposed = false;
        try
        {
            await drained.ConfigureAwait(false);
            await ObserveCompletionAsync(cancellation).ConfigureAwait(false);
            await ObserveCompletionAsync(lifetimeCancellation).ConfigureAwait(false);
            await ObserveCompletionAsync(providerCancel).ConfigureAwait(false);

            session.Lifetime.Dispose();
            lifetimeDisposed = true;
            await session.ProviderSession.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (!lifetimeDisposed)
            {
                session.Lifetime.Dispose();
            }
        }
    }

    private static async Task ObserveCompletionAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A completed fault still permits one provider disposal attempt.
        }
    }

    private static Task<bool> InterruptProviderAsync(SessionState session, AgentTurnId turnId)
    {
        lock (session.Gate)
        {
            return session.InterruptTask ??= StartInterruptProvider(session, turnId);
        }
    }

    private static Task<bool> StartInterruptProvider(SessionState session, AgentTurnId turnId)
    {
        session.ProviderCancelTask = Task.Run(() => session.ProviderSession.CancelTurnAsync(turnId, CancellationToken.None));
        return AwaitInterruptionAsync(session.ProviderCancelTask);
    }

    private static async Task<bool> AwaitInterruptionAsync(Task cancelTask)
    {
        try
        {
            await cancelTask.WaitAsync(StopTimeout).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<bool> DrainTurnAsync(
        SessionState session,
        AgentTurnId turnId,
        CancellationTokenSource turnCts,
        TaskCompletionSource turnDrained,
        IAsyncEnumerator<AgentProviderEvent>? enumerator,
        Task<bool>? moveTask)
    {
        var success = true;
        try
        {
            if (moveTask is not null)
            {
                try
                {
                    await moveTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The adapter already ended the pending MoveNext call.
                }
            }

            Task? cancellation;
            Task? lifetimeCancellation;
            lock (session.Gate)
            {
                cancellation = session.CancellationTask;
                lifetimeCancellation = session.LifetimeCancellationTask;
            }

            await ObserveCompletionAsync(cancellation).ConfigureAwait(false);
            await ObserveCompletionAsync(lifetimeCancellation).ConfigureAwait(false);

            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            success = false;
        }
        finally
        {
            lock (session.Gate)
            {
                if (session.ActiveTurn == turnId)
                {
                    session.ActiveTurn = null;
                    session.ActiveCancellation = null;
                }
            }

            turnCts.Dispose();
            turnDrained.TrySetResult();
        }

        return success;
    }

    private sealed class SessionState(IAgentSession providerSession)
    {
        public object Gate { get; } = new();

        public IAgentSession ProviderSession { get; } = providerSession;

        public CancellationTokenSource Lifetime { get; } = new();

        public HashSet<AgentTurnId> TurnIds { get; } = [];

        public AgentTurnId? ActiveTurn { get; set; }

        public CancellationTokenSource? ActiveCancellation { get; set; }

        public TaskCompletionSource? TurnDrained { get; set; }

        public Task? CancellationTask { get; set; }

        public Task? LifetimeCancellationTask { get; set; }

        public Task? ProviderCancelTask { get; set; }

        public Task<bool>? InterruptTask { get; set; }

        public Task<bool>? ShutdownTask { get; set; }

        public bool Closed { get; set; }

        public long Sequence;
    }
}
