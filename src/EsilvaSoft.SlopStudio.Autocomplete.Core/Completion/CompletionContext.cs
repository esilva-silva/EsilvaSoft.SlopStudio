using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

/// <summary>All semantic inputs are captured before a completion request leaves the editor thread.</summary>
public sealed record CompletionContext(
    TextSnapshotVersion Version,
    EditorDialects Dialect,
    SymbolKinds ExpectedKinds,
    string Prefix,
    TextSpan ReplaceSpan)
{
    public string ParentPath { get; init; } = "";
    /// <summary>Catalog shape that constrained this request, when structural context could be established.</summary>
    public string? ShapeId { get; init; }
    public string? ValueType { get; init; }
    public CatalogScope? Scope { get; init; }
    public IReadOnlyList<CollectionSchema> LocalSchemas { get; init; } = [];
    public IReadOnlyList<CatalogSymbol> LocalSymbols { get; init; } = [];
    public int MaximumItems { get; init; } = 100;
    public int MaximumCandidates { get; init; } = 200;
    public MetadataAccess CatalogAccess { get; init; } = MetadataAccess.Peek;
}
