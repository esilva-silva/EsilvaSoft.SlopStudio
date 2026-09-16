using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Language;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Fixture used by tests of <see cref="MetadataCache"/> and its consumers (metadata caching domain).</summary>
internal sealed class FakeMetadataSource : IMongoMetadataSource
{
    private int _calls;
    private int _completed;
    public int Calls => Volatile.Read(ref _calls);
    public int Completed => Volatile.Read(ref _completed);
    public TaskCompletionSource? Gate { get; set; }
    public bool IgnoreCancellation { get; set; }
    public Exception? Failure { get; set; }
    public Func<string, IReadOnlyList<string>> Databases { get; set; } = _ => ["loja", "auditoria"];
    public Func<string, IReadOnlyList<CollectionDefinition>> Collections { get; set; } = _ => [new("clientes", CollectionKind.Collection, null), new("pedidos", CollectionKind.View, null)];
    public IReadOnlyList<IndexInfo> Indexes { get; set; } = [];
    public IReadOnlyList<SampledDocument> Sample { get; set; } = [];

    public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Call(() => Databases(profile.Name), cancellationToken);
    public Task<IReadOnlyList<string>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<string>>(() => Collections(database).Select(definition => definition.Name).ToArray(), cancellationToken);
    public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
        Call(() => Collections(database).SingleOrDefault(definition => definition.Name == collection), cancellationToken);
    public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => Call(() => Indexes, cancellationToken);
    public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) =>
        Call(() => Sample, cancellationToken);

    private async Task<T> Call<T>(Func<T> value, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        try
        {
            if (Gate is { } gate) await (IgnoreCancellation ? gate.Task : gate.Task.WaitAsync(cancellationToken));
            if (Failure is { } failure) throw failure;
            return value();
        }
        finally { Interlocked.Increment(ref _completed); }
    }
}
