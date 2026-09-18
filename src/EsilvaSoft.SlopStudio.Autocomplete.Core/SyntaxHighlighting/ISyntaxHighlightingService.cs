namespace EsilvaSoft.SlopStudio.Autocomplete.Core.SyntaxHighlighting;

public interface ISyntaxHighlightingService
{
    SyntaxSnapshot Highlight(string text, SyntaxLanguage language, SyntaxContext? context = null,
        SyntaxSnapshot? previous = null, CancellationToken cancellationToken = default);
}
