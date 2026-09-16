namespace EsilvaSoft.SlopStudio.Application.Language.Completion;

public sealed record SnippetExpansion(string Text, IReadOnlyList<SnippetPlaceholder> Placeholders);
