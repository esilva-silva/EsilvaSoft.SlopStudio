namespace EsilvaSoft.SlopStudio.Application;

public enum ApplicationOperationStatus { Idle, Running, Success, Warning, Error, Cancelled }
public enum ApplicationOperationPriority { Low, Normal, High }

public sealed record ApplicationOperation(Guid Id, string Description, double? Progress, bool CanCancel,
    ApplicationOperationStatus Status, ApplicationOperationPriority Priority, DateTimeOffset StartedAt)
{
    public bool IsIndeterminate => Progress is null;
}

public interface IApplicationOperationService
{
    IReadOnlyList<ApplicationOperation> ActiveOperations { get; }
    ApplicationOperation? LastCompleted { get; }
    event EventHandler? Changed;
    ApplicationOperationScope Begin(string description, ApplicationOperationPriority priority = ApplicationOperationPriority.Normal,
        bool canCancel = true, CancellationToken cancellationToken = default);
    void Cancel(Guid id);
}

/// <summary>Independent cancellation and immutable snapshots; subscribers marshal notifications to their UI dispatcher.</summary>
public sealed class ApplicationOperationService : IApplicationOperationService
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ApplicationOperationScope> _active = [];
    private ApplicationOperation? _lastCompleted;
    public event EventHandler? Changed;
    public IReadOnlyList<ApplicationOperation> ActiveOperations
    {
        get { lock (_gate) return _active.Values.Select(s => s.Snapshot).OrderByDescending(s => s.Priority).ThenBy(s => s.StartedAt).ToArray(); }
    }
    public ApplicationOperation? LastCompleted { get { lock (_gate) return _lastCompleted; } }

    public ApplicationOperationScope Begin(string description, ApplicationOperationPriority priority = ApplicationOperationPriority.Normal,
        bool canCancel = true, CancellationToken cancellationToken = default)
    {
        var scope = new ApplicationOperationScope(this, description, priority, canCancel, cancellationToken);
        lock (_gate) _active.Add(scope.Snapshot.Id, scope);
        Changed?.Invoke(this, EventArgs.Empty);
        return scope;
    }

    public void Cancel(Guid id)
    {
        ApplicationOperationScope? scope;
        lock (_gate) _active.TryGetValue(id, out scope);
        scope?.Cancel();
    }

    internal void Update(ApplicationOperationScope scope, bool completed)
    {
        lock (_gate)
        {
            if (!_active.ContainsKey(scope.Snapshot.Id)) return;
            if (completed) { _active.Remove(scope.Snapshot.Id); _lastCompleted = scope.Snapshot; }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class ApplicationOperationScope : IDisposable
{
    private readonly ApplicationOperationService _owner;
    private readonly CancellationTokenSource _cancellation;
    private readonly object _gate = new();
    private ApplicationOperation _snapshot;
    private long _lastProgress;
    private bool _completed;
    private bool _disposed;
    public ApplicationOperation Snapshot { get { lock (_gate) return _snapshot; } }
    public CancellationToken Token { get; }

    internal ApplicationOperationScope(ApplicationOperationService owner, string description, ApplicationOperationPriority priority, bool canCancel, CancellationToken token)
    {
        _owner = owner;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Token = _cancellation.Token;
        _snapshot = new(Guid.NewGuid(), description, null, canCancel, ApplicationOperationStatus.Running, priority, DateTimeOffset.UtcNow);
    }

    public void Report(long completed, long total, string? description = null)
    {
        lock (_gate)
        {
            if (_completed || Environment.TickCount64 - _lastProgress < 100 && !(total > 0 && completed >= total && _snapshot.Progress != 100)) return;
            _lastProgress = Environment.TickCount64;
            _snapshot = _snapshot with { Description = description ?? _snapshot.Description,
                Progress = total > 0 ? Math.Clamp(100d * completed / total, 0, 100) : null };
        }
        _owner.Update(this, false);
    }

    public void Complete(ApplicationOperationStatus status = ApplicationOperationStatus.Success, string? description = null)
    {
        if (status is ApplicationOperationStatus.Idle or ApplicationOperationStatus.Running) throw new ArgumentOutOfRangeException(nameof(status));
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            _snapshot = _snapshot with { Status = status, Description = description ?? _snapshot.Description, CanCancel = false };
        }
        _owner.Update(this, true);
    }

    internal void Cancel()
    {
        lock (_gate)
        {
            if (_disposed || _completed || !_snapshot.CanCancel) return;
            // Schedule callbacks: a driver callback must never block the status-bar click.
            _ = _cancellation.CancelAsync();
        }
    }

    public void Dispose()
    {
        Complete(Token.IsCancellationRequested ? ApplicationOperationStatus.Cancelled : ApplicationOperationStatus.Warning);
        lock (_gate) { if (_disposed) return; _disposed = true; _cancellation.Dispose(); }
    }
}
