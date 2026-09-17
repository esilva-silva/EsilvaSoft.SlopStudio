using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

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
