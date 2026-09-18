namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

public sealed record SnippetExpansion(string Text, IReadOnlyList<SnippetPlaceholder> Placeholders);
