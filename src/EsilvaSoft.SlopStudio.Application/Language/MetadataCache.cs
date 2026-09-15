using System.Diagnostics;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Language;

/// <summary>
/// Immutable metadata snapshots per connection. Reads never block or perform I/O on the caller: a missing or expired value
/// schedules one background load per key (single-flight), stale values are served while refreshing, failures back off,
/// and only connected profiles are ever loaded.
/// </summary>
public sealed class MetadataCache : IMetadataCache, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<MetadataKey, Entry> _entries = [];
    private readonly Dictionary<ConnectionIdentity, ConnectionState> _connections = [];
    private readonly HashSet<Guid> _samplingProfiles = [];
    private readonly IMongoMetadataSource _source;
    private readonly IApplicationOperationService? _operations;
    private readonly IMetadataInvalidationBus? _invalidations;
    private readonly MetadataCacheOptions _options;
    private readonly TimeProvider _clock;
    private long _accessCounter;
    private bool _disposed;

    public MetadataCache(IMongoMetadataSource source, IApplicationOperationService? operations = null, IMetadataInvalidationBus? invalidations = null,
        MetadataCacheOptions? options = null, TimeProvider? timeProvider = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _operations = operations;
        _invalidations = invalidations;
        _options = options ?? new MetadataCacheOptions();
        _clock = timeProvider ?? TimeProvider.System;
        if (_invalidations is not null) _invalidations.Published += OnInvalidationPublished;
    }

    public event EventHandler<MetadataChangedEventArgs>? Changed;

    public IReadOnlyCollection<Guid> SchemaSamplingProfiles
    {
        get { lock (_gate) return _samplingProfiles.ToArray(); }
    }

    public bool IsConnected(ConnectionIdentity connection)
    {
        lock (_gate) return _connections.ContainsKey(connection);
    }

    public void Connect(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var identity = ConnectionIdentity.From(profile);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connections.ContainsKey(identity)) return;
            _connections[identity] = new ConnectionState(profile);
        }
    }

    public void Disconnect(Guid profileId)
    {
        ConnectionState[] states;
        lock (_gate)
        {
            states = _connections.Where(pair => pair.Key.ProfileId == profileId).Select(pair => pair.Value).ToArray();
            foreach (var state in states) _connections.Remove(ConnectionIdentity.From(state.Profile));
            foreach (var key in _entries.Keys.Where(key => key.Connection.ProfileId == profileId).ToArray()) _entries.Remove(key);
        }
        foreach (var state in states) state.Dispose();
        RaiseChanged(profileId);
    }

    public void SetSchemaSamplingAllowed(Guid profileId, bool allowed)
    {
        lock (_gate)
        {
            if (allowed) _samplingProfiles.Add(profileId); else _samplingProfiles.Remove(profileId);
        }
    }

    public MetadataView<IReadOnlyList<string>> GetDatabases(ConnectionIdentity connection, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<IReadOnlyList<string>>(new(connection, MetadataScope.Databases), access);

    public MetadataView<IReadOnlyList<CollectionEntry>> GetCollections(ConnectionIdentity connection, string database, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<IReadOnlyList<CollectionEntry>>(new(connection, MetadataScope.Collections, database), access);

    public MetadataView<CollectionMetadata> GetDefinition(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<CollectionMetadata>(new(connection, MetadataScope.Definition, database, collection), access);

    public MetadataView<IReadOnlyList<IndexInfo>> GetIndexes(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<IReadOnlyList<IndexInfo>>(new(connection, MetadataScope.Indexes, database, collection), access);

    public MetadataView<CollectionSchema> GetSampledSchema(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<CollectionSchema>(new(connection, MetadataScope.SampledSchema, database, collection), access);

    public void PutDatabases(ConnectionProfile profile, IReadOnlyList<string> databases)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(databases);
        var identity = ConnectionIdentity.From(profile);
        var names = databases.ToArray();
        var present = names.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            // A database that disappeared from the listing takes its collections and indexes with it.
            foreach (var key in _entries.Keys.Where(key => key.Connection == identity && key.Database.Length > 0 && !present.Contains(key.Database)).ToArray())
                _entries.Remove(key);
            Store(new(identity, MetadataScope.Databases), names);
        }
        RaiseChanged(profile.Id);
    }

    public void PutCollections(ConnectionProfile profile, string database, IReadOnlyList<string> collections)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(collections);
        var identity = ConnectionIdentity.From(profile);
        var present = collections.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            var known = KnownKinds(identity, database);
            foreach (var key in _entries.Keys.Where(key => key.Connection == identity && key.Database == database && key.Collection.Length > 0 && !present.Contains(key.Collection)).ToArray())
                _entries.Remove(key);
            Store(new(identity, MetadataScope.Collections, database),
                collections.Select(name => new CollectionEntry(name, known.GetValueOrDefault(name, CollectionKind.Unknown))).ToArray());
        }
        RaiseChanged(profile.Id);
    }

    public void PutIndexes(ConnectionProfile profile, string database, string collection, IReadOnlyList<IndexInfo> indexes)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(indexes);
        lock (_gate) Store(new(ConnectionIdentity.From(profile), MetadataScope.Indexes, database, collection), indexes.ToArray());
        RaiseChanged(profile.Id);
    }

    public Task RefreshAsync(MetadataKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        Task loading;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connections.TryGetValue(key.Connection, out var state))
                throw new InvalidOperationException("Conecte a origem antes de atualizar os metadados.");
            if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry();
            entry.Loading ??= StartLoad(key, entry, state);
            loading = entry.Loading;
        }
        return loading.WaitAsync(cancellationToken);
    }

    public async Task<CollectionSchema> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        options = (options ?? new SchemaSampleOptions()).Validate();
        using var operation = _operations?.Begin($"Amostrando schema — {collection}", ApplicationOperationPriority.Normal, canCancel: true, cancellationToken);
        var token = operation?.Token ?? cancellationToken;
        try
        {
            var documents = await _source.SampleSchemaAsync(profile, database, collection, options, token).ConfigureAwait(false);
            var schema = new SchemaBuilder(_options.SchemaMaximumDepth, _options.SchemaMaximumNodes).AddSample(documents).Build();
            lock (_gate) Store(new(ConnectionIdentity.From(profile), MetadataScope.SampledSchema, database, collection), schema);
            operation?.Complete(ApplicationOperationStatus.Success, $"Schema amostrado — {documents.Count} documento(s), {schema.NodeCount} campo(s); somente nomes e tipos");
            RaiseChanged(profile.Id);
            return schema;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            operation?.Complete(ApplicationOperationStatus.Cancelled, "Amostragem de schema cancelada");
            throw;
        }
        catch
        {
            operation?.Complete(ApplicationOperationStatus.Error, "Amostragem de schema indisponível");
            throw;
        }
    }

    public void Invalidate(MetadataInvalidation invalidation)
    {
        ArgumentNullException.ThrowIfNull(invalidation);
        var changed = false;
        lock (_gate)
        {
            foreach (var (key, entry) in _entries.ToArray())
            {
                if (key.Connection.ProfileId != invalidation.ProfileId || !Affects(invalidation, key)) continue;
                if (invalidation.Strength == InvalidationStrength.Strong) _entries.Remove(key);
                else entry.ForcedStale = true;
                changed = true;
            }
        }
        if (changed) RaiseChanged(invalidation.ProfileId);
    }

    public IReadOnlyList<MetadataNamespace> SnapshotNamespaces(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        var result = new List<MetadataNamespace>();
        lock (_gate)
        {
            foreach (var (key, entry) in _entries)
            {
                if (result.Count >= maximum) break;
                switch (entry.Value)
                {
                    case IReadOnlyList<string> databases when key.Scope == MetadataScope.Databases:
                        result.AddRange(databases.Select(database => new MetadataNamespace(key.Connection.ProfileId, database)));
                        break;
                    case IReadOnlyList<CollectionEntry> collections:
                        result.AddRange(collections.Select(collection => new MetadataNamespace(key.Connection.ProfileId, key.Database, collection.Name)));
                        break;
                    case IReadOnlyList<IndexInfo> indexes:
                        result.AddRange(indexes.Select(index => new MetadataNamespace(key.Connection.ProfileId, key.Database, key.Collection, index.Name)));
                        break;
                }
            }
        }
        return result.Count > maximum ? result.GetRange(0, maximum) : result;
    }

    public void Dispose()
    {
        ConnectionState[] states;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            states = _connections.Values.ToArray();
            _connections.Clear();
            _entries.Clear();
        }
        if (_invalidations is not null) _invalidations.Published -= OnInvalidationPublished;
        foreach (var state in states) state.Dispose();
    }

    private void OnInvalidationPublished(object? sender, MetadataInvalidationEventArgs e) => Invalidate(e.Invalidation);

    private MetadataView<T> Get<T>(MetadataKey key, MetadataAccess access) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        MetadataView<T> view;
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            _entries.TryGetValue(key, out var entry);
            if (entry is not null) entry.LastAccess = ++_accessCounter;
            if (access == MetadataAccess.LoadIfNeeded && !_disposed && _connections.TryGetValue(key.Connection, out var state) && CanLoad(key)
                && (entry is null || entry.Loading is null && IsExpired(entry, key.Scope, now) && now >= entry.RetryAfter))
            {
                if (entry is null) _entries[key] = entry = new Entry { LastAccess = ++_accessCounter };
                entry.Loading = StartLoad(key, entry, state);
            }
            view = View<T>(entry, key.Scope, now);
        }
        AutocompleteMetrics.MetadataCacheLookup.Add(1, new KeyValuePair<string, object?>("scope", key.Scope.ToString()),
            new KeyValuePair<string, object?>("result", view.Value is null ? "miss" : view.Freshness == MetadataFreshness.Fresh ? "fresh" : "stale"));
        return view;
    }

    private MetadataView<T> View<T>(Entry? entry, MetadataScope scope, DateTimeOffset now) where T : class
    {
        if (entry is null) return new(null, MetadataFreshness.Unknown, null, false);
        var value = entry.Value as T;
        var loading = entry.Loading is not null;
        var freshness = value is null
            ? loading ? MetadataFreshness.Loading : entry.Failures > 0 ? MetadataFreshness.Failed : MetadataFreshness.Unknown
            : entry.Failures > 0 || IsExpired(entry, scope, now) ? MetadataFreshness.Stale : MetadataFreshness.Fresh;
        return new(value, freshness, value is null ? null : entry.LoadedAt, loading);
    }

    // Caller holds _gate. Task.Run keeps the source off the calling thread even when it completes synchronously.
    private Task StartLoad(MetadataKey key, Entry entry, ConnectionState state) => Task.Run(() => LoadAsync(key, entry, state));

    private async Task LoadAsync(MetadataKey key, Entry entry, ConnectionState state)
    {
        var started = Stopwatch.GetTimestamp();
        object? value = null;
        var outcome = "success";
        try
        {
            using var operation = _operations?.Begin($"Atualizando metadados — {state.Profile.Name}", ApplicationOperationPriority.Low, canCancel: true, state.Token);
            var token = operation?.Token ?? state.Token;
            try
            {
                value = await FetchAsync(key, state.Profile, token).ConfigureAwait(false);
                operation?.Complete(ApplicationOperationStatus.Success, $"Metadados atualizados — {state.Profile.Name}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                outcome = "cancelled";
                operation?.Complete(ApplicationOperationStatus.Cancelled, "Atualização de metadados cancelada");
            }
            catch (Exception)
            {
                // Messages from the driver may include hosts or filters; only the state is published.
                outcome = "failed";
                operation?.Complete(ApplicationOperationStatus.Warning, $"Metadados indisponíveis — {state.Profile.Name}");
            }
        }
        catch (ObjectDisposedException)
        {
            outcome = "cancelled";
        }
        bool stored;
        lock (_gate)
        {
            // Invalidation, disconnection or disposal replaced the entry: a late result is discarded.
            stored = !_disposed && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry);
            entry.Loading = null;
            if (stored)
            {
                var now = _clock.GetUtcNow();
                if (value is not null)
                {
                    if (value is CollectionEntry[] fetched) value = WithKnownKinds(key, fetched);
                    entry.Value = value; entry.LoadedAt = now; entry.ForcedStale = false; entry.Failures = 0; entry.RetryAfter = default;
                    if (value is CollectionMetadata metadata) UpdateKind(key, metadata.Kind);
                    if (key.IsCollectionScoped) Evict(key.Connection);
                }
                else if (outcome == "failed")
                {
                    entry.Failures++;
                    entry.RetryAfter = now + _options.Backoff[Math.Min(entry.Failures, _options.Backoff.Count) - 1];
                }
            }
        }
        AutocompleteMetrics.MetadataRefreshDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("scope", key.Scope.ToString()), new KeyValuePair<string, object?>("outcome", stored ? outcome : "discarded"));
        if (stored && value is not null) RaiseChanged(key.Connection.ProfileId);
    }

    private async Task<object> FetchAsync(MetadataKey key, ConnectionProfile profile, CancellationToken token)
    {
        switch (key.Scope)
        {
            case MetadataScope.Databases:
                return (await _source.ListDatabaseNamesAsync(profile, token).ConfigureAwait(false)).ToArray();
            case MetadataScope.Collections:
                return (await _source.ListCollectionNamesAsync(profile, key.Database, token).ConfigureAwait(false))
                    .Select(name => new CollectionEntry(name, CollectionKind.Unknown)).ToArray();
            case MetadataScope.Definition:
                var definition = await _source.GetCollectionDefinitionAsync(profile, key.Database, key.Collection, token).ConfigureAwait(false);
                var validator = definition?.ValidatorJson is { } json
                    ? new SchemaBuilder(_options.SchemaMaximumDepth, _options.SchemaMaximumNodes).AddJsonSchema(json).Build() : null;
                return new CollectionMetadata(definition?.Kind ?? CollectionKind.Unknown,
                    validator is not null && validator.Evidence.HasFlag(EvidenceSources.Validator) ? validator : null);
            case MetadataScope.Indexes:
                return (await _source.ListIndexesAsync(profile, key.Database, key.Collection, token).ConfigureAwait(false)).ToArray();
            case MetadataScope.SampledSchema:
                var sample = await _source.SampleSchemaAsync(profile, key.Database, key.Collection, new SchemaSampleOptions(), token).ConfigureAwait(false);
                return new SchemaBuilder(_options.SchemaMaximumDepth, _options.SchemaMaximumNodes).AddSample(sample).Build();
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }
    }

    // Caller holds _gate.
    private void Store(MetadataKey key, object value)
    {
        if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry();
        entry.Value = value;
        entry.LoadedAt = _clock.GetUtcNow();
        entry.ForcedStale = false;
        entry.Failures = 0;
        entry.RetryAfter = default;
        entry.LastAccess = ++_accessCounter;
        if (key.IsCollectionScoped) Evict(key.Connection);
    }

    // Caller holds _gate. A definition carries the collection type that nameOnly listings omit.
    private void UpdateKind(MetadataKey key, CollectionKind kind)
    {
        if (kind == CollectionKind.Unknown || !_entries.TryGetValue(new(key.Connection, MetadataScope.Collections, key.Database), out var entry)
            || entry.Value is not IReadOnlyList<CollectionEntry> collections) return;
        var updated = collections.ToArray();
        for (var index = 0; index < updated.Length; index++)
            if (updated[index].Name == key.Collection && updated[index].Kind != kind) { updated[index] = updated[index] with { Kind = kind }; entry.Value = updated; return; }
    }

    // Caller holds _gate. A refreshed name listing keeps types already learned from definitions.
    private CollectionEntry[] WithKnownKinds(MetadataKey key, CollectionEntry[] fetched)
    {
        var kinds = KnownKinds(key.Connection, key.Database);
        return fetched.Select(entry => entry.Kind == CollectionKind.Unknown && kinds.TryGetValue(entry.Name, out var kind) ? entry with { Kind = kind } : entry).ToArray();
    }

    // Caller holds _gate.
    private Dictionary<string, CollectionKind> KnownKinds(ConnectionIdentity identity, string database)
    {
        var kinds = new Dictionary<string, CollectionKind>(StringComparer.Ordinal);
        foreach (var (key, entry) in _entries)
        {
            if (key.Connection != identity || key.Database != database) continue;
            if (entry.Value is CollectionMetadata { Kind: not CollectionKind.Unknown } metadata) kinds.TryAdd(key.Collection, metadata.Kind);
            else if (entry.Value is IReadOnlyList<CollectionEntry> collections)
                foreach (var collection in collections) if (collection.Kind != CollectionKind.Unknown) kinds.TryAdd(collection.Name, collection.Kind);
        }
        return kinds;
    }

    // Caller holds _gate.
    private void Evict(ConnectionIdentity connection)
    {
        var scoped = _entries.Where(pair => pair.Key.Connection == connection && pair.Key.IsCollectionScoped && pair.Value.Value is not null && pair.Value.Loading is null)
            .OrderBy(pair => pair.Value.LastAccess).ToArray();
        for (var index = 0; index < scoped.Length - _options.CollectionScopedEntriesPerConnection; index++) _entries.Remove(scoped[index].Key);
    }

    // Caller holds _gate.
    private bool CanLoad(MetadataKey key) => key.Scope != MetadataScope.SampledSchema || _samplingProfiles.Contains(key.Connection.ProfileId);

    private bool IsExpired(Entry entry, MetadataScope scope, DateTimeOffset now) =>
        entry.Value is null || entry.ForcedStale || now - entry.LoadedAt >= scope switch
        {
            MetadataScope.Databases => _options.DatabasesTtl,
            MetadataScope.Collections => _options.CollectionsTtl,
            MetadataScope.Definition => _options.DefinitionTtl,
            MetadataScope.Indexes => _options.IndexesTtl,
            _ => _options.SampledSchemaTtl
        };

    private static bool Affects(MetadataInvalidation invalidation, MetadataKey key) => invalidation.Change switch
    {
        MetadataChange.Connection => true,
        MetadataChange.Databases => key.Scope == MetadataScope.Databases || invalidation.Database.Length > 0 && key.Database == invalidation.Database,
        MetadataChange.Collections => key.Database == invalidation.Database
            && (key.Scope == MetadataScope.Collections || invalidation.Collection.Length > 0 && key.Collection == invalidation.Collection),
        MetadataChange.Indexes => key.Scope == MetadataScope.Indexes && key.Database == invalidation.Database
            && (invalidation.Collection.Length == 0 || key.Collection == invalidation.Collection),
        MetadataChange.Validation => key.Scope == MetadataScope.Definition && key.Database == invalidation.Database
            && (invalidation.Collection.Length == 0 || key.Collection == invalidation.Collection),
        _ => false
    };

    private void RaiseChanged(Guid profileId) => Changed?.Invoke(this, new(profileId));

    private sealed class Entry
    {
        public object? Value { get; set; }
        public DateTimeOffset LoadedAt { get; set; }
        public bool ForcedStale { get; set; }
        public Task? Loading { get; set; }
        public int Failures { get; set; }
        public DateTimeOffset RetryAfter { get; set; }
        public long LastAccess { get; set; }
    }

    private sealed class ConnectionState(ConnectionProfile profile) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        public ConnectionProfile Profile { get; } = profile;
        public CancellationToken Token => _cancellation.Token;

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }
}
