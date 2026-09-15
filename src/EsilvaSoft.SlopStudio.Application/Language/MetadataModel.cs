using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Language;

/// <summary>
/// Identity of a connection for metadata: profile, explicit target and a hash of the saved connection string.
/// Editing a profile or routing to another instance never reuses metadata from the previous configuration.
/// </summary>
public sealed record ConnectionIdentity(Guid ProfileId, string? TargetHost, string Fingerprint)
{
    public static ConnectionIdentity From(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(profile.ConnectionString + "\n" + profile.TargetHost));
        return new(profile.Id, profile.TargetHost, Convert.ToHexString(hash, 0, 8));
    }
}

public enum MetadataScope { Databases, Collections, Definition, Indexes, SampledSchema }

public sealed record MetadataKey(ConnectionIdentity Connection, MetadataScope Scope, string Database = "", string Collection = "")
{
    /// <summary>Collection-scoped values are bounded by the per-connection LRU.</summary>
    public bool IsCollectionScoped => Scope is MetadataScope.Definition or MetadataScope.Indexes or MetadataScope.SampledSchema;
}

public enum MetadataFreshness { Unknown, Loading, Fresh, Stale, Failed }

/// <summary>Peek never schedules a remote load; LoadIfNeeded may start a background refresh.</summary>
public enum MetadataAccess { LoadIfNeeded, Peek }

/// <summary>A cached value and its state. A stale value stays usable while it is refreshed.</summary>
public readonly record struct MetadataView<T>(T? Value, MetadataFreshness Freshness, DateTimeOffset? LoadedAt, bool IsRefreshing) where T : class;

public enum CollectionKind { Unknown, Collection, View, TimeSeries }

public sealed record CollectionEntry(string Name, CollectionKind Kind);

/// <summary>One collection as returned by listCollections filtered by name.</summary>
public sealed record CollectionDefinition(string Name, CollectionKind Kind, string? ValidatorJson);

/// <summary>
/// Type and declared validator schema of one collection. Loaded per collection, never for a whole database,
/// so memory and transfer stay bounded by the collections actually edited.
/// </summary>
public sealed record CollectionMetadata(CollectionKind Kind, CollectionSchema? Validator);

public sealed record MetadataNamespace(Guid ProfileId, string Database, string Collection = "", string Index = "");

public sealed record SchemaSampleOptions(int Size = 100, int Depth = 4, int MaxTimeMs = 2000)
{
    public SchemaSampleOptions Validate()
    {
        if (Size is < 1 or > 1000 || Depth is < 1 or > 8 || MaxTimeMs is < 1 or > 60000)
            throw new ArgumentException("Amostra de schema: 1–1000 documentos, profundidade 1–8 e tempo máximo de 1–60000 ms.");
        return this;
    }
}

public sealed record MetadataCacheOptions
{
    public TimeSpan DatabasesTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan CollectionsTtl { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan DefinitionTtl { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan IndexesTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan SampledSchemaTtl { get; init; } = TimeSpan.FromMinutes(30);
    /// <summary>Delay before retrying after consecutive failures; the last value repeats.</summary>
    public IReadOnlyList<TimeSpan> Backoff { get; init; } = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];
    /// <summary>LRU bound for index and sampled schema entries per connection.</summary>
    public int CollectionScopedEntriesPerConnection { get; init; } = 64;
    public int SchemaMaximumDepth { get; init; } = 12;
    public int SchemaMaximumNodes { get; init; } = 10_000;
}

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

public enum MetadataChange { Databases, Collections, Indexes, Validation, Connection }

public enum InvalidationStrength { Strong, Soft }

/// <summary>A metadata change caused by the IDE. Strong removes cached values; soft only marks them stale.</summary>
public sealed record MetadataInvalidation(Guid ProfileId, MetadataChange Change, string Database = "", string Collection = "",
    InvalidationStrength Strength = InvalidationStrength.Strong);

public sealed class MetadataInvalidationEventArgs(MetadataInvalidation invalidation) : EventArgs
{
    public MetadataInvalidation Invalidation { get; } = invalidation;
}

public sealed class MetadataChangedEventArgs(Guid profileId) : EventArgs
{
    public Guid ProfileId { get; } = profileId;
}

public interface IMetadataInvalidationBus
{
    event EventHandler<MetadataInvalidationEventArgs>? Published;
    void Publish(MetadataInvalidation invalidation);
}

public sealed class MetadataInvalidationBus : IMetadataInvalidationBus
{
    public event EventHandler<MetadataInvalidationEventArgs>? Published;

    public void Publish(MetadataInvalidation invalidation)
    {
        ArgumentNullException.ThrowIfNull(invalidation);
        Published?.Invoke(this, new(invalidation));
    }
}

public interface IMetadataCache
{
    event EventHandler<MetadataChangedEventArgs>? Changed;
    bool IsConnected(ConnectionIdentity connection);
    /// <summary>Only connected profiles are ever loaded remotely.</summary>
    void Connect(ConnectionProfile profile);
    /// <summary>Cancels loads and removes every value of the profile.</summary>
    void Disconnect(Guid profileId);
    void SetSchemaSamplingAllowed(Guid profileId, bool allowed);
    IReadOnlyCollection<Guid> SchemaSamplingProfiles { get; }
    MetadataView<IReadOnlyList<string>> GetDatabases(ConnectionIdentity connection, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    MetadataView<IReadOnlyList<CollectionEntry>> GetCollections(ConnectionIdentity connection, string database, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    MetadataView<CollectionMetadata> GetDefinition(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    MetadataView<IReadOnlyList<IndexInfo>> GetIndexes(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    /// <summary>Loaded automatically only for profiles that opted in; otherwise only by <see cref="SampleSchemaAsync"/>.</summary>
    MetadataView<CollectionSchema> GetSampledSchema(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    void PutDatabases(ConnectionProfile profile, IReadOnlyList<string> databases);
    void PutCollections(ConnectionProfile profile, string database, IReadOnlyList<string> collections);
    void PutIndexes(ConnectionProfile profile, string database, string collection, IReadOnlyList<IndexInfo> indexes);
    /// <summary>Loads the key now, ignoring TTL and backoff. The profile must be connected.</summary>
    Task RefreshAsync(MetadataKey key, CancellationToken cancellationToken = default);
    /// <summary>Explicit user action: samples names and types and stores the schema.</summary>
    Task<CollectionSchema> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions? options = null, CancellationToken cancellationToken = default);
    void Invalidate(MetadataInvalidation invalidation);
    /// <summary>Loaded namespaces only; never schedules loads.</summary>
    IReadOnlyList<MetadataNamespace> SnapshotNamespaces(int maximum);
}

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
