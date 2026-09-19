using EsilvaSoft.SlopStudio.Application.SchemaLearning;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// In-memory <see cref="ILearnedSchemaRepository"/> for L13 tests. L14's real LiteDB-backed implementation is out
/// of scope for this lote; this fake exists only to drive <see cref="SchemaLearningCoordinator"/>.
/// </summary>
internal sealed class FakeLearnedSchemaRepository : ILearnedSchemaRepository
{
    private readonly List<SchemaObservationDelta> _applied = [];
    private int _applyCount;

    /// <summary>Overrides the default "always applied" outcome; receives the delta and the worker's token.</summary>
    public Func<SchemaObservationDelta, CancellationToken, Task<SchemaCommitResult>>? OnApply { get; set; }

    public int ApplyCount => Volatile.Read(ref _applyCount);

    public IReadOnlyList<SchemaObservationDelta> Applied
    {
        get { lock (_applied) return _applied.ToArray(); }
    }

    public async Task<SchemaCommitResult> ApplyAsync(SchemaObservationDelta delta, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _applyCount);
        lock (_applied) _applied.Add(delta);
        if (OnApply is not null) return await OnApply(delta, cancellationToken).ConfigureAwait(false);
        return new SchemaCommitResult(SchemaCommitOutcome.Applied, _applyCount, null);
    }

    public Task<LearnedSchemaSnapshot?> GetAsync(LearnedSchemaKey key, CancellationToken cancellationToken) =>
        Task.FromResult<LearnedSchemaSnapshot?>(null);

    public Task<IReadOnlyList<LearnedSchemaKey>> ListKeysAsync(Guid profileId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LearnedSchemaKey>>([]);

    public Task<bool> WasBatchCommittedAsync(LearnedSchemaKey key, Guid batchId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task RenameAsync(LearnedSchemaKey source, LearnedSchemaKey destination, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<int> RemoveAsync(LearnedSchemaKey key, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<int> RemoveDatabaseAsync(Guid profileId, string database, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<int> RemoveProfileAsync(Guid profileId, CancellationToken cancellationToken) => Task.FromResult(0);
}
