namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Process-local admission control owned by the single tool registry instance.</summary>
internal sealed class AgentToolInvocationQuota
{
    private const int MaximumPerSession = 2;
    private const int MaximumPerConnection = 4;
    private const int MaximumGlobal = 8;
    private const int MaximumPerTurn = 20;
    private const int MaximumTrackedTurns = 4_096;
    private static readonly TimeSpan TurnIdleLifetime = TimeSpan.FromHours(1);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, int> _sessions = [];
    private readonly Dictionary<Guid, int> _connections = [];
    private readonly Dictionary<(Guid SessionId, Guid TurnId), TurnUsage> _turns = [];
    private int _global;

    public Lease? TryEnter(Guid sessionId, Guid turnId, Guid? connectionId)
    {
        if (sessionId == Guid.Empty || turnId == Guid.Empty)
            throw new ArgumentException("A session and turn are required for quota admission.");
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            PruneIdleTurns(now);
            var turnKey = (sessionId, turnId);
            if (!_turns.TryGetValue(turnKey, out var usage))
            {
                // If all slots still belong to live turns, deny new turns instead of evicting
                // their counters and silently granting another 20 calls.
                if (_turns.Count >= MaximumTrackedTurns) return null;
                usage = new TurnUsage(0, now);
            }
            if (usage.Count >= MaximumPerTurn) return null;
            _turns[turnKey] = usage with { Count = usage.Count + 1, LastSeenUtc = now };

            if (_global >= MaximumGlobal ||
                _sessions.GetValueOrDefault(sessionId) >= MaximumPerSession ||
                connectionId is { } id && _connections.GetValueOrDefault(id) >= MaximumPerConnection)
                return null;

            _global++;
            _sessions[sessionId] = _sessions.GetValueOrDefault(sessionId) + 1;
            if (connectionId is { } connection)
                _connections[connection] = _connections.GetValueOrDefault(connection) + 1;
            return new Lease(this, sessionId, connectionId);
        }
    }

    private void Leave(Guid sessionId, Guid? connectionId)
    {
        lock (_gate)
        {
            _global--;
            Decrement(_sessions, sessionId);
            if (connectionId is { } connection) Decrement(_connections, connection);
        }
    }

    private static void Decrement(Dictionary<Guid, int> counters, Guid key)
    {
        var remaining = counters[key] - 1;
        if (remaining == 0) counters.Remove(key);
        else counters[key] = remaining;
    }

    private void PruneIdleTurns(DateTimeOffset now)
    {
        foreach (var key in _turns.Where(item => now - item.Value.LastSeenUtc > TurnIdleLifetime)
                     .Select(item => item.Key).ToArray())
            _turns.Remove(key);
    }

    private readonly record struct TurnUsage(int Count, DateTimeOffset LastSeenUtc);

    internal sealed class Lease : IDisposable
    {
        private AgentToolInvocationQuota? _owner;
        private readonly Guid _sessionId;
        private readonly Guid? _connectionId;
        private int _references = 1;
        private int _disposed;

        internal Lease(AgentToolInvocationQuota owner, Guid sessionId, Guid? connectionId)
        {
            _owner = owner;
            _sessionId = sessionId;
            _connectionId = connectionId;
        }

        // A source that ignores cancellation still occupies its slot until its task finishes.
        public void HoldUntil(Task operation)
        {
            if (operation.IsCompleted)
            {
                _ = operation.Exception;
                return;
            }
            Interlocked.Increment(ref _references);
            _ = operation.ContinueWith(completed =>
            {
                _ = completed.Exception;
                ReleaseReference();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) ReleaseReference();
        }

        private void ReleaseReference()
        {
            if (Interlocked.Decrement(ref _references) == 0)
                Interlocked.Exchange(ref _owner, null)?.Leave(_sessionId, _connectionId);
        }
    }
}
