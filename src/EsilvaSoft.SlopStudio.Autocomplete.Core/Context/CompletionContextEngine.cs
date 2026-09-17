using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Context;

/// <summary>
/// Converts a snapshot and caret into completion intent. It only consumes the shared tolerant lexer: no metadata, code
/// evaluation, or document mutation is permitted here. The shallow structural rules deliberately prefer a safe broad
/// result to a misleading narrow one while the full shape walker is still unavailable.
/// </summary>
public sealed class CompletionContextEngine
{
    public static CompletionContextAnalysis Analyze(ContextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caret = request.ValidCaret;
        var text = request.Snapshot.GetText(0, request.Snapshot.Length);
        var tokens = new List<MongoToken>();
        MongoLexer.Tokenize(text.AsSpan(), tokens, mode: request.Dialect == EditorDialects.AggregationJson ? MongoLexerMode.Json : MongoLexerMode.Script,
            cancellationToken: cancellationToken);

        var tokenIndex = FindTokenAt(tokens, caret);
        if (tokenIndex >= 0 && IsNonCompletable(tokens[tokenIndex])) return Empty(request, CompletionCursorRole.NonCompletable, caret);

        var tabTarget = request.TabScope is { } tabScope
            ? new NamespaceTarget(null, tabScope.Database, tabScope.Collection, NamespaceTargetConfidence.TabDefault) { Connection = tabScope.Connection }
            : null;
        var target = NamespaceTargetResolver.Resolve(text, caret, tabTarget, request.Dialect, cancellationToken);

        var token = tokenIndex >= 0 ? tokens[tokenIndex] : default;
        var prefix = tokenIndex >= 0 && token.Kind is MongoTokenKind.Identifier or MongoTokenKind.String
            ? TextBeforeCaret(text, token, caret) : string.Empty;
        var replace = tokenIndex >= 0 && token.Kind == MongoTokenKind.String
            ? StringContentSpan(text, token)
            : tokenIndex >= 0 && token.Kind == MongoTokenKind.Identifier
                ? new TextSpan(token.Start, Math.Max(0, token.End - token.Start))
                : new TextSpan(caret, 0);
        var insert = new TextSpan(replace.Start, Math.Max(0, caret - replace.Start));

        var role = Classify(text, tokens, tokenIndex, caret, request.Dialect, out var expected, out var quote);
        if (role is CompletionCursorRole.NonCompletable) return Empty(request, role, caret);
        var shape = ShapeWalker.Walk(text, caret, role, cancellationToken: cancellationToken);
        if (shape.ExpectedKinds != SymbolKinds.None)
            expected = role == CompletionCursorRole.PropertyValue ? expected | shape.ExpectedKinds : shape.ExpectedKinds;

        var context = new CompletionContext(request.Snapshot.Version, request.Dialect, expected, prefix, replace)
        {
            Scope = target.Confidence == NamespaceTargetConfidence.TabDefault
                ? request.TabScope
                : target.Connection is { } connection
                    ? new CatalogScope(connection, target.Database ?? "", target.Collection ?? "")
                    : null,
            ShapeId = shape.ShapeId,
            ParentPath = shape.ParentPath,
            CatalogAccess = MetadataAccess.Peek
        };
        return new(context, role, insert, quote, false, target);
    }

    private static CompletionContextAnalysis Empty(ContextRequest request, CompletionCursorRole role, int caret) =>
        new(new CompletionContext(request.Snapshot.Version, request.Dialect, SymbolKinds.None, string.Empty, new TextSpan(caret, 0)),
            role, new TextSpan(caret, 0), null, false, NamespaceTarget.Unknown);

    private static CompletionCursorRole Classify(string text, List<MongoToken> tokens, int current, int caret,
        EditorDialects dialect, out SymbolKinds expected, out char? quote)
    {
        quote = null;
        if (current >= 0 && tokens[current].Kind == MongoTokenKind.String)
        {
            quote = text[tokens[current].Start];
            var content = TextBeforeCaret(text, tokens[current], caret);
            if (content.StartsWith("$$", StringComparison.Ordinal)) { expected = SymbolKinds.SystemVariable | SymbolKinds.LocalVariable; return CompletionCursorRole.VariableReferenceString; }
            if (content.StartsWith('$')) { expected = SymbolKinds.Field; return CompletionCursorRole.FieldReferenceString; }
            expected = SymbolKinds.Collection;
            return PreviousText(tokens, text, current) is "[" ? CompletionCursorRole.IndexerString : CompletionCursorRole.StringArgument;
        }

        var exclusive = current < 0 ? tokens.Count : tokens[current].Kind == MongoTokenKind.Identifier ? current : current + 1;
        var previous = PreviousText(tokens, text, exclusive);
        if (previous == ".")
        {
            var receiver = PreviousText(tokens, text, current);
            expected = receiver == "db" ? SymbolKinds.Collection | SymbolKinds.DatabaseMethod : SymbolKinds.Methods | SymbolKinds.Field;
            return CompletionCursorRole.MemberAccess;
        }
        if (previous is "{" or ",") { expected = SymbolKinds.Field | SymbolKinds.QueryOperator | SymbolKinds.AggregationStage; return CompletionCursorRole.PropertyKey; }
        if (previous == "[") { expected = dialect == EditorDialects.AggregationJson ? SymbolKinds.AggregationStage : SymbolKinds.Field | SymbolKinds.Snippet; return CompletionCursorRole.ArrayElement; }
        if (previous == ":") { expected = SymbolKinds.Operators | SymbolKinds.BsonConstructor | SymbolKinds.Snippet; return CompletionCursorRole.PropertyValue; }
        if (previous is "(" or ",") { expected = SymbolKinds.Field | SymbolKinds.Snippet | SymbolKinds.LocalVariable; return CompletionCursorRole.CallArgument; }
        expected = SymbolKinds.DslRoot | SymbolKinds.GlobalFunction | SymbolKinds.Keyword | SymbolKinds.Snippet | SymbolKinds.LocalVariable;
        return string.IsNullOrWhiteSpace(text[..caret]) || previous is ";" or "}" ? CompletionCursorRole.StatementStart : CompletionCursorRole.Unknown;
    }

    private static bool IsNonCompletable(MongoToken token) => token.Kind is MongoTokenKind.LineComment or MongoTokenKind.BlockComment or MongoTokenKind.Regex or MongoTokenKind.Number;
    private static int FindTokenAt(List<MongoToken> tokens, int caret)
    {
        for (var index = 0; index < tokens.Count; index++) if (tokens[index].Span.IntersectsWith(caret)) return index;
        return -1;
    }
    private static int PreviousIndex(List<MongoToken> tokens, int exclusive) => Math.Clamp(exclusive - 1, -1, tokens.Count - 1);
    private static string PreviousText(List<MongoToken> tokens, string text, int exclusive)
    {
        var index = PreviousIndex(tokens, exclusive);
        return index < 0 ? string.Empty : text.Substring(tokens[index].Start, tokens[index].Length);
    }
    private static string TextBeforeCaret(string text, MongoToken token, int caret)
    {
        var start = token.Kind == MongoTokenKind.String ? token.Start + 1 : token.Start;
        return text.Substring(start, Math.Clamp(caret, start, token.End) - start);
    }

    private static TextSpan StringContentSpan(string text, MongoToken token)
    {
        var start = Math.Min(token.Start + 1, token.End);
        var end = token.End;
        if (end > start && text[end - 1] is '\'' or '"' or '`') end--;
        return new TextSpan(start, Math.Max(0, end - start));
    }
}
