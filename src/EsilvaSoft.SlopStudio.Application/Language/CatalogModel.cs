using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Language;

/// <summary>Kind of knowledge the IDE can suggest. <see cref="SymbolKinds"/> uses the same order as flags.</summary>
public enum SymbolKind
{
    Connection, Database, Collection, View, TimeSeriesCollection, Field, Index,
    DslRoot, ConnectionMethod, DatabaseMethod, CollectionMethod, CursorMethod, GlobalFunction,
    BsonConstructor, BsonType, QueryOperator, ProjectionOperator, UpdateOperator,
    AggregationStage, ExpressionOperator, Accumulator, WindowOperator, SystemVariable,
    SearchOperator, SearchOption, Keyword, Snippet, LocalVariable, EnvironmentKey
}

[Flags]
public enum SymbolKinds
{
    None = 0,
    Connection = 1 << 0, Database = 1 << 1, Collection = 1 << 2, View = 1 << 3, TimeSeriesCollection = 1 << 4, Field = 1 << 5, Index = 1 << 6,
    DslRoot = 1 << 7, ConnectionMethod = 1 << 8, DatabaseMethod = 1 << 9, CollectionMethod = 1 << 10, CursorMethod = 1 << 11, GlobalFunction = 1 << 12,
    BsonConstructor = 1 << 13, BsonType = 1 << 14, QueryOperator = 1 << 15, ProjectionOperator = 1 << 16, UpdateOperator = 1 << 17,
    AggregationStage = 1 << 18, ExpressionOperator = 1 << 19, Accumulator = 1 << 20, WindowOperator = 1 << 21, SystemVariable = 1 << 22,
    SearchOperator = 1 << 23, SearchOption = 1 << 24, Keyword = 1 << 25, Snippet = 1 << 26, LocalVariable = 1 << 27, EnvironmentKey = 1 << 28,
    Namespaces = Connection | Database | Collection | View | TimeSeriesCollection,
    Methods = ConnectionMethod | DatabaseMethod | CollectionMethod | CursorMethod,
    Operators = QueryOperator | ProjectionOperator | UpdateOperator | ExpressionOperator | Accumulator | WindowOperator
}

/// <summary>Editor surfaces. <see cref="Mql"/> symbols (operators, stages) are valid inside every dialect.</summary>
[Flags]
public enum EditorDialects
{
    None = 0,
    Console = 1,
    MongoshScript = 2,
    AggregationJson = 4,
    Mql = 8,
    Scripts = Console | MongoshScript,
    All = Console | MongoshScript | AggregationJson | Mql
}

[Flags]
public enum SymbolTraits { None = 0, Write = 1, Deprecated = 2, AtlasOnly = 4, Stale = 8 }

/// <summary>Where the knowledge about a field comes from.</summary>
[Flags]
public enum EvidenceSources { None = 0, Validator = 1, Index = 2, Results = 4, Sample = 8, History = 16 }

public static class SymbolKindExtensions
{
    public static SymbolKinds ToFlag(this SymbolKind kind) => (SymbolKinds)(1 << (int)kind);
}

/// <summary>Connection, database and collection that scope a metadata symbol.</summary>
public sealed record CatalogScope(ConnectionIdentity Connection, string Database = "", string Collection = "");

/// <summary>One suggestible piece of knowledge. Long documentation is resolved later and never stored here.</summary>
public sealed record CatalogSymbol(string Id, SymbolKind Kind, string Name, string Detail)
{
    public string Category { get; init; } = "";
    public EditorDialects Dialects { get; init; } = EditorDialects.Mql;
    public SymbolTraits Flags { get; init; }
    /// <summary>Shape of the value or argument, when the symbol takes one.</summary>
    public string? ValueShape { get; init; }
    public IReadOnlyList<string> Parameters { get; init; } = [];
    public string? Returns { get; init; }
    /// <summary>LSP snippet syntax: $1, ${1:placeholder}, ${1|a,b|}, $0 and \$ for a literal dollar.</summary>
    public string? Snippet { get; init; }
    /// <summary>BSON types of the field the operator applies to; empty means any type.</summary>
    public IReadOnlyList<string> ApplicableTypes { get; init; } = [];
    public string? Since { get; init; }
    public CatalogScope? Scope { get; init; }
    public EvidenceSources Evidence { get; init; }
    public FieldNode? Field { get; init; }
}

public enum CatalogMatch { Any, Prefix, Humps, Substring }

/// <summary>Ordered from best to worst for the user: a loading scope matters more than an unavailable one.</summary>
public enum CatalogCompleteness { Complete, Partial, Unavailable, Loading }

public sealed record CatalogCandidate(CatalogSymbol Symbol, CatalogMatch Match);

public sealed record CatalogResult(IReadOnlyList<CatalogCandidate> Candidates, CatalogCompleteness Completeness);

/// <summary>What the caller expects at the cursor. Only sources that provide the requested kinds are consulted.</summary>
public sealed record CatalogQuery(SymbolKinds Kinds, EditorDialects Dialect, string Prefix = "")
{
    /// <summary>Captured target connection; metadata kinds are unavailable without it.</summary>
    public ConnectionProfile? Connection { get; init; }
    public string Database { get; init; } = "";
    public string Collection { get; init; } = "";
    /// <summary>Parent field path whose children are requested; empty for root fields.</summary>
    public string ParentPath { get; init; } = "";
    /// <summary>Saved profiles offered for <see cref="SymbolKinds.Connection"/>.</summary>
    public IReadOnlyList<ConnectionProfile> Connections { get; init; } = [];
    /// <summary>Tab-local evidence, such as fields of results loaded in the tab.</summary>
    public IReadOnlyList<CollectionSchema> LocalSchemas { get; init; } = [];
    public int MaximumCandidates { get; init; } = 200;
    /// <summary>
    /// Applied to every metadata read of the query. Explicit invocation may use <see cref="MetadataAccess.LoadIfNeeded"/> for the requested scopes;
    /// automatic requests and refiltering use <see cref="MetadataAccess.Peek"/> and never schedule remote work.
    /// </summary>
    public MetadataAccess Access { get; init; } = MetadataAccess.LoadIfNeeded;
}

public interface ICatalogSource
{
    SymbolKinds ProvidedKinds { get; }
    /// <summary>Appends candidates from memory only; never performs I/O on the calling thread.</summary>
    CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken);
}

public interface IKnowledgeCatalog
{
    CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default);
}
