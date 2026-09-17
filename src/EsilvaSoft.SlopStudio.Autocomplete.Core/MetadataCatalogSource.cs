using System.Globalization;
using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Connections, databases, collections, indexes and fields from the metadata cache and tab-local evidence.</summary>
public sealed class MetadataCatalogSource(IMetadataCache cache) : ICatalogSource
{
    private const SymbolKinds MetadataKinds = SymbolKinds.Database | SymbolKinds.Collection | SymbolKinds.View | SymbolKinds.TimeSeriesCollection
        | SymbolKinds.Index | SymbolKinds.Field;
    private readonly IMetadataCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ConditionalWeakTable<object, object> _tables = new();
    private readonly object _mergeGate = new();
    private SchemaMemo? _lastMerge;

    public SymbolKinds ProvidedKinds => MetadataKinds | SymbolKinds.Connection;

    public CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sink);
        var completeness = CatalogCompleteness.Complete;
        if (Wants(query, SymbolKinds.Connection))
        {
            new NameTable<ConnectionProfile>(query.Connections, profile => profile.Name).Collect(query.Prefix, Remaining(query, sink), null,
                (profile, match) => sink.Add(new(new CatalogSymbol($"meta:{profile.Id:N}", SymbolKind.Connection, profile.Name, "Conexão") { Dialects = EditorDialects.Scripts }, match)));
        }
        if ((query.Kinds & MetadataKinds) == 0) return completeness;
        if (query.Connection is null) return CatalogCompleteness.Unavailable;
        var identity = ConnectionIdentity.From(query.Connection);
        var access = query.Access;
        cancellationToken.ThrowIfCancellationRequested();

        if (Wants(query, SymbolKinds.Database))
        {
            var view = _cache.GetDatabases(identity, access);
            completeness = Worst(completeness, Completeness(view));
            if (view.Value is { } databases)
                Table(databases, name => name).Collect(query.Prefix, Remaining(query, sink), null, (name, match) => sink.Add(new(new CatalogSymbol(
                    $"meta:{identity.ProfileId:N}/{name}", SymbolKind.Database, name, "Banco de dados")
                    { Dialects = EditorDialects.Scripts, Scope = new(identity, name), Flags = Flags(view) }, match)));
        }
        if (query.Database.Length == 0) return completeness;

        if (Wants(query, SymbolKinds.Collection | SymbolKinds.View | SymbolKinds.TimeSeriesCollection))
        {
            var view = _cache.GetCollections(identity, query.Database, access);
            completeness = Worst(completeness, Completeness(view));
            if (view.Value is { } collections)
                Table(collections, entry => entry.Name).Collect(query.Prefix, Remaining(query, sink), entry => Wants(query, KindOf(entry.Kind).ToFlag()),
                    (entry, match) => sink.Add(new(new CatalogSymbol($"meta:{identity.ProfileId:N}/{query.Database}/{entry.Name}", KindOf(entry.Kind), entry.Name, Describe(entry.Kind))
                        { Dialects = EditorDialects.Scripts, Scope = new(identity, query.Database, entry.Name), Flags = Flags(view) }, match)));
        }
        if (query.Collection.Length == 0) return completeness;

        if (Wants(query, SymbolKinds.Index))
        {
            var view = _cache.GetIndexes(identity, query.Database, query.Collection, access);
            completeness = Worst(completeness, Completeness(view));
            if (view.Value is { } indexes)
                Table(indexes, index => index.Name).Collect(query.Prefix, Remaining(query, sink), null, (index, match) => sink.Add(new(new CatalogSymbol(
                    $"meta:{identity.ProfileId:N}/{query.Database}/{query.Collection}/index/{index.Name}", SymbolKind.Index, index.Name, index.Keys)
                    { Dialects = EditorDialects.All, Scope = new(identity, query.Database, query.Collection), Flags = Flags(view) }, match)));
        }

        if (Wants(query, SymbolKinds.Field))
        {
            var definition = _cache.GetDefinition(identity, query.Database, query.Collection, access);
            var indexes = _cache.GetIndexes(identity, query.Database, query.Collection, access);
            var sampled = _cache.GetSampledSchema(identity, query.Database, query.Collection, access);
            completeness = Worst(completeness, Worst(Completeness(definition), Completeness(indexes)));
            var schema = MergedSchema(identity, query, definition.Value?.Validator, indexes.Value, sampled.Value);
            if (schema.Find(query.ParentPath) is { } parent)
                parent.Children.Collect(query.Prefix, Remaining(query, sink), null, (field, match) => sink.Add(new(new CatalogSymbol(
                    $"meta:{identity.ProfileId:N}/{query.Database}/{query.Collection}/field/{field.Path}", SymbolKind.Field, field.Name, Describe(field))
                    { Dialects = EditorDialects.All, Scope = new(identity, query.Database, query.Collection), Evidence = field.Evidence, Field = field }, match)));
        }
        return completeness;
    }

    internal static string Describe(FieldNode field)
    {
        var parts = new List<string> { field.Types.Count > 1 ? $"{field.PrimaryType} +{field.Types.Count - 1}" : field.PrimaryType };
        if (field.Occurrence is { } occurrence) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{occurrence * 100:0}%"));
        if (field.Flags.HasFlag(FieldTraits.Required)) parts.Add("obrigatório");
        if (field.Flags.HasFlag(FieldTraits.Indexed)) parts.Add("indexado");
        if (field.Flags.HasFlag(FieldTraits.ArrayOfDocuments)) parts.Add("array de documentos");
        else if (field.Flags.HasFlag(FieldTraits.Array)) parts.Add("array");
        return string.Join(" · ", parts);
    }

    // One entry is enough: consecutive keystrokes query the same collection with the same snapshots.
    private CollectionSchema MergedSchema(ConnectionIdentity identity, CatalogQuery query, CollectionSchema? validator, IReadOnlyList<IndexInfo>? indexes, CollectionSchema? sampled)
    {
        lock (_mergeGate)
        {
            if (_lastMerge is { } memo && memo.Connection == identity && memo.Database == query.Database && memo.Collection == query.Collection
                && ReferenceEquals(memo.Validator, validator) && ReferenceEquals(memo.Indexes, indexes) && ReferenceEquals(memo.Sampled, sampled)
                && ReferenceEquals(memo.Local, query.LocalSchemas))
                return memo.Schema;
        }
        var indexSchema = indexes is null ? null : new SchemaBuilder().AddIndexes(indexes).Build();
        var schema = CollectionSchema.Merge(new[] { validator, indexSchema, sampled }.Concat(query.LocalSchemas));
        lock (_mergeGate) _lastMerge = new(identity, query.Database, query.Collection, validator, indexes, sampled, query.LocalSchemas, schema);
        return schema;
    }

    private NameTable<T> Table<T>(IReadOnlyList<T> items, Func<T, string> name) where T : class =>
        (NameTable<T>)_tables.GetValue(items, list => new NameTable<T>((IReadOnlyList<T>)list, name));

    private static bool Wants(CatalogQuery query, SymbolKinds kinds) => (query.Kinds & kinds) != 0;

    private static int Remaining(CatalogQuery query, ICollection<CatalogCandidate> sink) => query.MaximumCandidates - sink.Count;

    private static CatalogCompleteness Worst(CatalogCompleteness first, CatalogCompleteness second) => first > second ? first : second;

    // Loading only while the cache reports a load in flight, which always ends with a terminal Changed; an unknown scope that nothing
    // is loading (Peek, disconnected, cancelled) is unavailable, so a list never waits for a notification that will not come.
    private static CatalogCompleteness Completeness<T>(MetadataView<T> view) where T : class => view switch
    {
        { Value: not null, Freshness: MetadataFreshness.Fresh } => CatalogCompleteness.Complete,
        { Value: not null } => CatalogCompleteness.Partial,
        { Freshness: MetadataFreshness.Loading } => CatalogCompleteness.Loading,
        { Freshness: MetadataFreshness.Failed } => CatalogCompleteness.Partial,
        _ => CatalogCompleteness.Unavailable
    };

    private static SymbolTraits Flags<T>(MetadataView<T> view) where T : class => view.Freshness == MetadataFreshness.Fresh ? SymbolTraits.None : SymbolTraits.Stale;

    private static SymbolKind KindOf(CollectionKind kind) => kind switch
    {
        CollectionKind.View => SymbolKind.View,
        CollectionKind.TimeSeries => SymbolKind.TimeSeriesCollection,
        _ => SymbolKind.Collection
    };

    private static string Describe(CollectionKind kind) => kind switch
    {
        CollectionKind.View => "View",
        CollectionKind.TimeSeries => "Coleção time series",
        _ => "Coleção"
    };

    private sealed record SchemaMemo(ConnectionIdentity Connection, string Database, string Collection, CollectionSchema? Validator,
        IReadOnlyList<IndexInfo>? Indexes, CollectionSchema? Sampled, IReadOnlyList<CollectionSchema> Local, CollectionSchema Schema);
}
