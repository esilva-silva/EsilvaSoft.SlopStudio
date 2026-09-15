using EsilvaSoft.SlopStudio.Application.Language.Syntax;

namespace EsilvaSoft.SlopStudio.Application.SyntaxHighlighting;

public enum SyntaxLanguage { Json, MongoScript, Aggregation, AtlasSearch }
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720", Justification = "String is the semantic name of the language token, not a variable type prefix.")]
public enum SyntaxTokenType
{
    Default, PropertyName, String, Number, Boolean, Null, Identifier, Keyword, Function, Method,
    Operator, Punctuation, Comment, Regex, MongoOperator, MongoStage, MongoFunction, MongoType,
    Connection, Database, Collection, Index, Error, Warning, Success, GhostText, MatchingBracket,
    UnmatchedBracket, SearchMatch, AtlasSearchOperator
}
public readonly record struct SyntaxToken(int Start, int Length, SyntaxTokenType Type);
public sealed record SyntaxNamespace(string Connection, string Database = "", string Collection = "", string Index = "");
public sealed record SyntaxContext(IReadOnlyList<SyntaxNamespace> Names, string Connection = "", string Database = "", string Collection = "")
{
    public static SyntaxContext Empty { get; } = new([]);
}
/// <summary>Central internal budgets; no text is truncated or changed by highlighting.</summary>
public static class SyntaxHighlightingOptions
{
    public const int DebounceMilliseconds = 80;
    public const int FullHighlightCharacters = 64 * 1024;
    public const int MaximumStyledTokens = 12000;
    public const int MaximumNesting = 512;
    public const int MaximumCachedLines = 100000;
    public const int LongLineThreshold = 64 * 1024;
    public const int LongLineWindowCharacters = 4096;
}
public interface ISyntaxHighlightingService
{
    SyntaxSnapshot Highlight(string text, SyntaxLanguage language, SyntaxContext? context = null,
        SyntaxSnapshot? previous = null, CancellationToken cancellationToken = default);
}
public sealed record SyntaxSnapshot(string Text, SyntaxLanguage Language, SyntaxContext Context,
    IReadOnlyList<SyntaxToken> Tokens, IReadOnlyDictionary<int, int> Brackets, int TokenizedLines)
{
    internal IReadOnlyList<SyntaxLine> Lines { get; init; } = [];
    public IReadOnlyList<int> LineStarts { get; init; } = [];
}
internal sealed record SyntaxLine(string Text, HighlightState Before, HighlightState After, SyntaxToken[] Tokens);
/// <summary>
/// State at a line boundary: the shared lexer state plus highlighting-only classification context (bracket frames,
/// pending $search/aggregate, previous tokens and the namespace being resolved). Value equality drives line reuse.
/// </summary>
internal sealed record HighlightState(MongoLexerState Lexer = default, string Frames = "", bool SearchPending = false,
    bool AggregatePending = false, string Previous = "", string BeforePrevious = "", string Connection = "", string Database = "", string Collection = "");
