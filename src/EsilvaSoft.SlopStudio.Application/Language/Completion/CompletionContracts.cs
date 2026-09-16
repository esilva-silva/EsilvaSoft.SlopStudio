using EsilvaSoft.SlopStudio.Application.Language.Text;

namespace EsilvaSoft.SlopStudio.Application.Language.Completion;

public enum CompletionItemKind : byte
{
    Text, Method, Function, Field, Variable, Class, Property, Keyword, Operator, Value, Snippet
}

public enum CompletionSource : byte { Catalog, Schema, Snippet, Local, Ai }

[Flags]
public enum CompletionItemTags : byte { None = 0, Deprecated = 1, Write = 2, Stale = 4, Ai = 8 }

public sealed record CompletionEdit(TextSpan InsertRange, TextSpan ReplaceRange, string NewText, bool IsSnippet = false);

/// <summary>Small, value-free reference retained by an item; its display text is built only for the highlighted item.</summary>
public sealed record CompletionDocumentationReference(
    string? Category = null,
    string? ValueShape = null,
    IReadOnlyList<string>? Parameters = null,
    string? Returns = null,
    string? Since = null,
    EvidenceSources Evidence = EvidenceSources.None);

/// <summary>Late presentation data for a completion item. It deliberately contains schema facts, never document values.</summary>
public sealed record CompletionDocumentation(string Title, string Text, CompletionItemKind Kind, CompletionItemTags Tags);

public sealed record CompletionItem(
    string SymbolId,
    string Label,
    string? LabelDetail,
    CompletionItemKind Kind,
    CompletionEdit Edit,
    string FilterText,
    double Score,
    CompletionSource Source)
{
    public IReadOnlyList<TextSpan> Highlights { get; init; } = [];
    public CompletionItemTags Tags { get; init; }
    public IReadOnlyList<char> CommitCharacters { get; init; } = [];
    public SymbolKind? CatalogKind { get; init; }
    public CompletionDocumentationReference? Documentation { get; init; }

    /// <summary>Nome determinístico para a árvore de automação, sem incluir valores de documentos.</summary>
    public string AccessibleName => string.IsNullOrWhiteSpace(LabelDetail)
        ? $"{Label}, tipo {Kind}"
        : $"{Label}, tipo {Kind}, {LabelDetail}";
}

public sealed record CompletionList(TextSnapshotVersion Version, IReadOnlyList<CompletionItem> Items, bool IsIncomplete)
{
    public static CompletionList Empty(TextSnapshotVersion version) => new(version, [], false);
}

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
