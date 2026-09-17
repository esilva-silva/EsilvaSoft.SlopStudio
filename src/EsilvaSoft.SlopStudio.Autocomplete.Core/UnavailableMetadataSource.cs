using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Source used when no driver is configured: write-through values still work, remote loads fail visibly.</summary>
public sealed class UnavailableMetadataSource : IMongoMetadataSource
{
    public static UnavailableMetadataSource Instance { get; } = new();
    private UnavailableMetadataSource() { }
    private static InvalidOperationException Error() => new("Fonte de metadados MongoDB não configurada.");
    public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => throw Error();
    public Task<IReadOnlyList<string>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) => throw Error();
    public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw Error();
    public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw Error();
    public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) => throw Error();
}
