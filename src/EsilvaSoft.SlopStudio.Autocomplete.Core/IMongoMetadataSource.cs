using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Driver access to metadata. Implementations receive the captured profile and never expose the URI.</summary>
public interface IMongoMetadataSource
{
    Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken);
    /// <summary>Names the user may list (nameOnly with authorizedCollections).</summary>
    Task<IReadOnlyList<string>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken);
    /// <summary>Type and validator of one collection; null when it does not exist.</summary>
    Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken);
    Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken);
    /// <summary>Field names and BSON types computed on the server; document values never leave it.</summary>
    Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken);
}
