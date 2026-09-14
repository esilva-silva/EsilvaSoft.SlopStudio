namespace EsilvaSoft.SlopStudio.Application.SyntaxHighlighting;

/// <summary>Fault-tolerant visual lexer. It does not parse, validate, execute or normalize BSON.</summary>
public sealed class SyntaxHighlightingService : ISyntaxHighlightingService
{
    public SyntaxSnapshot Highlight(string text, SyntaxLanguage language, SyntaxContext? context = null,
        SyntaxSnapshot? previous = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        context ??= SyntaxContext.Empty;
        var reusable = previous is not null && previous.Language == language && previous.Context == context;
        if (reusable && ReferenceEquals(previous!.Text, text)) return previous;
        var oldLines = reusable ? previous!.Lines : [];
        var lines = new List<SyntaxLine>();
        var tokens = new List<SyntaxToken>();
        var state = new LexerState(Connection: context.Connection, Database: context.Database, Collection: context.Collection);
        var starts = new List<(int Start, int Length)>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (text[i] != '\n') continue;
            starts.Add((start, i + 1 - start)); start = i + 1;
        }
        if (start < text.Length) starts.Add((start, text.Length - start));
        // Align unchanged suffixes even when new lines have been inserted/deleted.
        var suffix = 0;
        while (suffix < Math.Min(starts.Count, oldLines.Count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = starts[^(suffix + 1)];
            if (!text.AsSpan(span.Start, span.Length).SequenceEqual(oldLines[^(suffix + 1)].Text)) break;
            suffix++;
        }
        var processed = 0;
        for (var index = 0; index < starts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = starts[index];
            var oldIndex = index >= starts.Count - suffix ? oldLines.Count - (starts.Count - index) : index;
            var old = oldIndex >= 0 && oldIndex < oldLines.Count ? oldLines[oldIndex] : null;
            SyntaxLine line;
            if (old is not null && old.Before == state && text.AsSpan(span.Start, span.Length).SequenceEqual(old.Text)) line = old;
            else
            {
                var value = text.Substring(span.Start, span.Length);
                var before = state;
                var local = TokenizeLine(value, language, context, ref state, cancellationToken);
                line = new(value, before, state, local); processed++;
            }
            state = line.After;
            if (starts.Count <= SyntaxHighlightingOptions.MaximumCachedLines) lines.Add(line);
            foreach (var token in line.Tokens) tokens.Add(token with { Start = token.Start + span.Start });
        }
        var brackets = new Dictionary<int, int>();
        var stack = new Stack<(char Bracket, int Position)>();
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Type != SyntaxTokenType.Punctuation || token.Length != 1) continue;
            var ch = text[token.Start];
            if (ch is '(' or '[' or '{') stack.Push((ch, token.Start));
            else if (ch is ')' or ']' or '}')
            {
                if (stack.TryPeek(out var opening) && Matches(opening.Bracket, ch))
                {
                    stack.Pop(); brackets[opening.Position] = token.Start; brackets[token.Start] = opening.Position;
                }
                else brackets[token.Start] = -1;
            }
        }
        foreach (var opening in stack) brackets[opening.Position] = -1;
        return new(text, language, context, tokens, brackets, processed) { Lines = lines, LineStarts = starts.Select(s => s.Start).ToArray() };
    }

    private static bool Matches(char a, char b) => (a, b) is ('(', ')') or ('[', ']') or ('{', '}');
    private static bool Word(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
    private static int Next(string text, int i) { while (i < text.Length && char.IsWhiteSpace(text[i])) i++; return i; }

    private static List<SyntaxToken> TokenizeLine(string text, SyntaxLanguage language, SyntaxContext context,
        ref LexerState state, CancellationToken cancellationToken)
    {
        var tokens = new List<SyntaxToken>();
        var i = 0;
        while (i < text.Length)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var start = i;
            var ch = text[i];
            if (state.BlockComment)
            {
                var end = text.IndexOf("*/", i, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                state = state with { BlockComment = end < 0 };
                tokens.Add(new(start, i - start, SyntaxTokenType.Comment)); continue;
            }
            if (state.Quote != '\0' || ch is '"' or '\'' or '`')
            {
                var continuing = state.Quote != '\0';
                var quote = continuing ? state.Quote : ch;
                if (!continuing) i++;
                var closed = false;
                while (i < text.Length)
                {
                    if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (text[i] == '\\') { i = Math.Min(i + 2, text.Length); continue; }
                    if (text[i++] == quote) { closed = true; break; }
                }
                state = state with { Quote = closed ? '\0' : quote };
                var end = Next(text, i);
                var value = !continuing && closed ? text[(start + 1)..(i - 1)] : "";
                var property = !continuing && closed && end < text.Length && text[end] == ':';
                var type = property ? ClassifyProperty(value, language, state) : SyntaxTokenType.String;
                if (!property && closed && !continuing) type = ClassifyName(value, type, context, ref state);
                tokens.Add(new(start, i - start, type));
                if (property) state = Pending(value, state);
                if (closed) state = Shift(state, property ? value : "<value>");
                continue;
            }
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            { tokens.Add(new(i, text.Length - i, SyntaxTokenType.Comment)); break; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                var end = text.IndexOf("*/", i, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                state = state with { BlockComment = end < 0 };
                tokens.Add(new(start, i - start, SyntaxTokenType.Comment)); continue;
            }
            if (ch == '/' && language != SyntaxLanguage.Json && CanStartRegex(state.Previous))
            {
                i++; var inClass = false;
                while (i < text.Length)
                {
                    if (text[i] == '\\') { i = Math.Min(text.Length, i + 2); continue; }
                    if (text[i] == '[') inClass = true;
                    if (text[i] == ']') inClass = false;
                    if (text[i++] == '/' && !inClass) break;
                }
                while (i < text.Length && char.IsLetter(text[i])) i++;
                tokens.Add(new(start, i - start, SyntaxTokenType.Regex)); state = Shift(state, "<value>"); continue;
            }
            if (char.IsDigit(ch) || ch == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1]) && CanStartRegex(state.Previous))
            {
                if (ch == '-') i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] is '.' or '_')) i++;
                if (i < text.Length && text[i] is 'e' or 'E')
                { i++; if (i < text.Length && text[i] is '+' or '-') i++; while (i < text.Length && char.IsDigit(text[i])) i++; }
                tokens.Add(new(start, i - start, SyntaxTokenType.Number)); state = Shift(state, "<value>"); continue;
            }
            if (Word(ch))
            {
                while (i < text.Length && Word(text[i])) i++;
                var value = text[start..i]; var next = Next(text, i);
                if (value == "db") state = state with { Connection = context.Connection, Database = context.Database, Collection = context.Collection };
                var property = next < text.Length && text[next] == ':';
                var call = next < text.Length && text[next] == '(';
                var type = property ? ClassifyProperty(value, language, state) : value switch
                {
                    "true" or "false" => SyntaxTokenType.Boolean,
                    "null" or "undefined" => SyntaxTokenType.Null,
                    _ when MongoSyntaxVocabulary.ExtendedJsonTypes.Contains(value) && call => SyntaxTokenType.MongoType,
                    _ when MongoSyntaxVocabulary.DslFunctions.Contains(value) && call => SyntaxTokenType.Function,
                    _ when MongoSyntaxVocabulary.Functions.Contains(value) && call => SyntaxTokenType.MongoFunction,
                    _ when MongoSyntaxVocabulary.Keywords.Contains(value) && language != SyntaxLanguage.Json => SyntaxTokenType.Keyword,
                    _ when value.StartsWith('$') => SyntaxTokenType.MongoOperator,
                    _ when call => state.Previous == "." ? SyntaxTokenType.Method : SyntaxTokenType.Function,
                    _ => SyntaxTokenType.Identifier
                };
                if (!property && !call) type = ClassifyName(value, type, context, ref state);
                tokens.Add(new(start, i - start, type));
                state = Pending(value, state);
                state = Shift(state, value); continue;
            }
            i++;
            var punctuation = "{}[]():,;.".Contains(ch, StringComparison.Ordinal);
            tokens.Add(new(start, 1, punctuation ? SyntaxTokenType.Punctuation : SyntaxTokenType.Operator));
            if (ch is '{' or '[' or '(')
            {
                var frame = ch == '{' && state.SearchPending ? 'S' : ch == '[' && (state.AggregatePending || language is SyntaxLanguage.Aggregation or SyntaxLanguage.AtlasSearch) ? 'A' : ch;
                if (state.Frames.Length < SyntaxHighlightingOptions.MaximumNesting) state = state with { Frames = state.Frames + frame };
                if (ch == '{') state = state with { SearchPending = false };
                if (ch == '[') state = state with { AggregatePending = false };
            }
            else if (ch is '}' or ']' or ')' && state.Frames.Length > 0) state = state with { Frames = state.Frames[..^1] };
            state = Shift(state, ch.ToString());
        }
        return tokens;
    }

    private static bool CanStartRegex(string previous) => previous is "" or "(" or "[" or "{" or ":" or "," or "=" or "return" or "=>" or "!" or ";";
    private static LexerState Shift(LexerState state, string value) => state with { BeforePrevious = state.Previous, Previous = value };
    private static LexerState Pending(string value, LexerState state) => state with
    {
        SearchPending = value is "$search" or "$searchMeta" || state.SearchPending,
        AggregatePending = value == "aggregate" || state.AggregatePending
    };
    private static SyntaxTokenType ClassifyProperty(string value, SyntaxLanguage language, LexerState state)
    {
        if (MongoSyntaxVocabulary.ExtendedJsonTypes.Contains(value)) return SyntaxTokenType.MongoType;
        if (MongoSyntaxVocabulary.AggregationStages.Contains(value)
            && (value is not ("$set" or "$unset") || language is SyntaxLanguage.Aggregation or SyntaxLanguage.AtlasSearch || state.Frames.Contains('A')))
            return SyntaxTokenType.MongoStage;
        if (value.StartsWith('$')) return SyntaxTokenType.MongoOperator;
        if (MongoSyntaxVocabulary.AtlasSearchOperators.Contains(value) && (language == SyntaxLanguage.AtlasSearch || state.Frames.Contains('S')))
            return SyntaxTokenType.AtlasSearchOperator;
        return SyntaxTokenType.PropertyName;
    }
    private static SyntaxTokenType ClassifyName(string value, SyntaxTokenType fallback, SyntaxContext context, ref LexerState state)
    {
        var connection = state.Connection; var database = state.Database; var collection = state.Collection;
        var previous = state.Previous; var before = state.BeforePrevious;
        if (previous == "(" && before == "getConnection" || previous == "." && MongoSyntaxVocabulary.DslRoots.Contains(before))
        {
            if (context.Names.Any(n => n.Connection == value))
            { state = state with { Connection = value, Database = "", Collection = "" }; return SyntaxTokenType.Connection; }
        }
        if (previous == "(" && before is "getDatabase" or "GetDatabase" or "getSiblingDB" or "getDB"
            || previous == "." && before == connection)
        {
            if (context.Names.Any(n => n.Connection == connection && n.Database == value))
            { state = state with { Database = value, Collection = "" }; return SyntaxTokenType.Database; }
        }
        if (previous == "(" && before is "getCollection" or "GetCollection" || previous == "." && (before == "db" || before == database))
        {
            if (context.Names.Any(n => n.Connection == connection && n.Database == database && n.Collection == value))
            { state = state with { Collection = value }; return SyntaxTokenType.Collection; }
        }
        if (previous == "(" && before is "dropIndex" or "hint" || previous == ":" && before == "name")
            if (context.Names.Any(n => n.Connection == connection && n.Database == database && n.Collection == collection && n.Index == value)) return SyntaxTokenType.Index;
        return fallback;
    }
}
