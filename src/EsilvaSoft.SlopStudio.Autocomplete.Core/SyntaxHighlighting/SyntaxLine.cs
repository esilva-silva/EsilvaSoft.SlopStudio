namespace EsilvaSoft.SlopStudio.Autocomplete.Core.SyntaxHighlighting;

internal sealed record SyntaxLine(string Text, HighlightState Before, HighlightState After, SyntaxToken[] Tokens);
