using System.Buffers;
using EsilvaSoft.SlopStudio.Application.Language.Syntax;

namespace EsilvaSoft.SlopStudio.Application.SyntaxHighlighting;

/// <summary>
/// Fault-tolerant visual classification over <see cref="MongoLexer"/> tokens, with a per-line cache. It does not parse,
/// validate, execute or normalize BSON; MongoDB meaning comes from the language vocabulary and loaded namespace names.
/// </summary>
public sealed class SyntaxHighlightingService : ISyntaxHighlightingService
{
    private const string OperandMarker = "<value>";
    private static readonly string[] AsciiStrings = Enumerable.Range(0, 128).Select(c => ((char)c).ToString()).ToArray();

    public SyntaxSnapshot Highlight(string text, SyntaxLanguage language, SyntaxContext? context = null,
        SyntaxSnapshot? previous = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        context ??= SyntaxContext.Empty;
        var reusable = previous is not null && previous.Language == language && previous.Context == context;
        if (reusable && ReferenceEquals(previous!.Text, text)) return previous;
        var oldLines = reusable ? previous!.Lines : [];
        var state = new HighlightState(Connection: context.Connection, Database: context.Database, Collection: context.Collection);
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
        var mode = language == SyntaxLanguage.Json ? MongoLexerMode.Json : MongoLexerMode.Script;
        var lines = new SyntaxLine[starts.Count];
        var processed = 0;
        var total = 0;
        var frames = ArrayPool<char>.Shared.Rent(SyntaxHighlightingOptions.MaximumNesting);
        var scratch = new List<SyntaxToken>();
        try
        {
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
                    var local = TokenizeLine(value, mode, language, context, ref state, frames, scratch, cancellationToken);
                    line = new(value, before, state, local); processed++;
                }
                state = line.After;
                lines[index] = line;
                total += line.Tokens.Length;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(frames);
        }
        var tokens = new SyntaxToken[total];
        var position = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var offset = starts[index].Start;
            foreach (var token in lines[index].Tokens) tokens[position++] = token with { Start = token.Start + offset };
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
        return new(text, language, context, tokens, brackets, processed)
        {
            Lines = starts.Count <= SyntaxHighlightingOptions.MaximumCachedLines ? lines : [],
            LineStarts = starts.Select(s => s.Start).ToArray()
        };
    }

    private static bool Matches(char a, char b) => (a, b) is ('(', ')') or ('[', ']') or ('{', '}');

    private static SyntaxToken[] TokenizeLine(string line, MongoLexerMode mode, SyntaxLanguage language, SyntaxContext context,
        ref HighlightState state, char[] frames, List<SyntaxToken> tokens, CancellationToken cancellationToken)
    {
        tokens.Clear();
        var classifier = new Classifier(state, frames, language, context);
        var lexer = new MongoLexer(line, state.Lexer, mode, 0, cancellationToken);
        while (lexer.TryRead(out var token))
        {
            var type = token.Kind switch
            {
                MongoTokenKind.LineComment or MongoTokenKind.BlockComment => SyntaxTokenType.Comment,
                MongoTokenKind.String or MongoTokenKind.Template => classifier.Literal(line, token),
                MongoTokenKind.Regex => classifier.Operand(SyntaxTokenType.Regex),
                MongoTokenKind.Number => classifier.Operand(SyntaxTokenType.Number),
                MongoTokenKind.Identifier => classifier.Word(line.Substring(token.Start, token.Length), token.Traits),
                _ => classifier.Symbol(line[token.Start], token.Kind)
            };
            tokens.Add(new(token.Start, token.Length, type));
        }
        state = classifier.ToState(lexer.State, state);
        return tokens.ToArray();
    }

    /// <summary>Mutable working copy of <see cref="HighlightState"/> for one line; materialized once at the end of the line.</summary>
    private struct Classifier
    {
        private readonly char[] _frames;
        private readonly SyntaxLanguage _language;
        private readonly SyntaxContext _context;
        private int _depth, _searchFrames, _aggregateFrames;
        private bool _searchPending, _aggregatePending;
        private string _previous, _beforePrevious, _connection, _database, _collection;

        public Classifier(HighlightState state, char[] frames, SyntaxLanguage language, SyntaxContext context)
        {
            _frames = frames; _language = language; _context = context;
            state.Frames.CopyTo(frames);
            _depth = state.Frames.Length;
            _searchFrames = state.Frames.AsSpan().Count('S');
            _aggregateFrames = state.Frames.AsSpan().Count('A');
            _searchPending = state.SearchPending; _aggregatePending = state.AggregatePending;
            _previous = state.Previous; _beforePrevious = state.BeforePrevious;
            _connection = state.Connection; _database = state.Database; _collection = state.Collection;
        }

        public readonly HighlightState ToState(MongoLexerState lexer, HighlightState before)
        {
            var frames = before.Frames.AsSpan().SequenceEqual(_frames.AsSpan(0, _depth)) ? before.Frames : new string(_frames, 0, _depth);
            return new(lexer, frames, _searchPending, _aggregatePending, _previous, _beforePrevious, _connection, _database, _collection);
        }

        public SyntaxTokenType Literal(string line, MongoToken token)
        {
            var continuing = token.Has(MongoTokenTraits.Continuation);
            var closed = token.IsTerminated;
            if (!continuing && closed && token.Has(MongoTokenTraits.FollowedByColon))
            {
                var value = line.Substring(token.Start + 1, token.Length - 2);
                var property = ClassifyProperty(value);
                Pending(value); Shift(value);
                return property;
            }
            var type = closed && !continuing ? ClassifyName(line.AsSpan(token.Start + 1, token.Length - 2), null, SyntaxTokenType.String) : SyntaxTokenType.String;
            if (closed) Shift(OperandMarker);
            return type;
        }

        public SyntaxTokenType Operand(SyntaxTokenType type)
        {
            Shift(OperandMarker);
            return type;
        }

        public SyntaxTokenType Word(string value, MongoTokenTraits flags)
        {
            if (value == "db") { _connection = _context.Connection; _database = _context.Database; _collection = _context.Collection; }
            var property = (flags & MongoTokenTraits.FollowedByColon) != 0;
            var call = (flags & MongoTokenTraits.FollowedByOpenParenthesis) != 0;
            var type = property ? ClassifyProperty(value) : value switch
            {
                "true" or "false" => SyntaxTokenType.Boolean,
                "null" or "undefined" => SyntaxTokenType.Null,
                _ when call && MongoSyntaxVocabulary.ExtendedJsonTypes.Contains(value) => SyntaxTokenType.MongoType,
                _ when call && MongoSyntaxVocabulary.DslFunctions.Contains(value) => SyntaxTokenType.Function,
                _ when call && MongoSyntaxVocabulary.Functions.Contains(value) => SyntaxTokenType.MongoFunction,
                _ when _language != SyntaxLanguage.Json && MongoSyntaxVocabulary.Keywords.Contains(value) => SyntaxTokenType.Keyword,
                _ when value.StartsWith('$') => SyntaxTokenType.MongoOperator,
                _ when call => _previous == "." ? SyntaxTokenType.Method : SyntaxTokenType.Function,
                _ => SyntaxTokenType.Identifier
            };
            if (!property && !call) type = ClassifyName(value, value, type);
            Pending(value); Shift(value);
            return type;
        }

        public SyntaxTokenType Symbol(char ch, MongoTokenKind kind)
        {
            if (ch is '{' or '[' or '(')
            {
                var frame = ch == '{' && _searchPending ? 'S'
                    : ch == '[' && (_aggregatePending || _language is SyntaxLanguage.Aggregation or SyntaxLanguage.AtlasSearch) ? 'A' : ch;
                if (_depth < SyntaxHighlightingOptions.MaximumNesting)
                {
                    _frames[_depth++] = frame;
                    if (frame == 'S') _searchFrames++;
                    else if (frame == 'A') _aggregateFrames++;
                }
                if (ch == '{') _searchPending = false;
                if (ch == '[') _aggregatePending = false;
            }
            else if (ch is '}' or ']' or ')' && _depth > 0)
            {
                var frame = _frames[--_depth];
                if (frame == 'S') _searchFrames--;
                else if (frame == 'A') _aggregateFrames--;
            }
            Shift(ch < AsciiStrings.Length ? AsciiStrings[ch] : ch.ToString());
            return kind == MongoTokenKind.Punctuation ? SyntaxTokenType.Punctuation : SyntaxTokenType.Operator;
        }

        private void Shift(string value) { _beforePrevious = _previous; _previous = value; }

        private void Pending(string value)
        {
            _searchPending = value is "$search" or "$searchMeta" || _searchPending;
            _aggregatePending = value == "aggregate" || _aggregatePending;
        }

        private readonly SyntaxTokenType ClassifyProperty(string value)
        {
            if (MongoSyntaxVocabulary.ExtendedJsonTypes.Contains(value)) return SyntaxTokenType.MongoType;
            if (MongoSyntaxVocabulary.AggregationStages.Contains(value)
                && (value is not ("$set" or "$unset") || _language is SyntaxLanguage.Aggregation or SyntaxLanguage.AtlasSearch || _aggregateFrames > 0))
                return SyntaxTokenType.MongoStage;
            if (value.StartsWith('$')) return SyntaxTokenType.MongoOperator;
            if (MongoSyntaxVocabulary.AtlasSearchOperators.Contains(value) && (_language == SyntaxLanguage.AtlasSearch || _searchFrames > 0))
                return SyntaxTokenType.AtlasSearchOperator;
            return SyntaxTokenType.PropertyName;
        }

        /// <summary>Namespace names only match loaded metadata; the value string is materialized only when it becomes state.</summary>
        private SyntaxTokenType ClassifyName(ReadOnlySpan<char> value, string? text, SyntaxTokenType fallback)
        {
            var names = _context.Names;
            if (names.Count == 0) return fallback;
            var connection = _connection; var database = _database; var collection = _collection;
            var previous = _previous; var before = _beforePrevious;
            if (previous == "(" && before == "getConnection" || previous == "." && MongoSyntaxVocabulary.DslRoots.Contains(before))
                for (var i = 0; i < names.Count; i++)
                    if (value.SequenceEqual(names[i].Connection))
                    { _connection = text ?? value.ToString(); _database = ""; _collection = ""; return SyntaxTokenType.Connection; }
            if (previous == "(" && before is "getDatabase" or "GetDatabase" or "getSiblingDB" or "getDB" || previous == "." && before == connection)
                for (var i = 0; i < names.Count; i++)
                    if (names[i].Connection == connection && value.SequenceEqual(names[i].Database))
                    { _database = text ?? value.ToString(); _collection = ""; return SyntaxTokenType.Database; }
            if (previous == "(" && before is "getCollection" or "GetCollection" || previous == "." && (before == "db" || before == database))
                for (var i = 0; i < names.Count; i++)
                    if (names[i].Connection == connection && names[i].Database == database && value.SequenceEqual(names[i].Collection))
                    { _collection = text ?? value.ToString(); return SyntaxTokenType.Collection; }
            if (previous == "(" && before is "dropIndex" or "hint" || previous == ":" && before == "name")
                for (var i = 0; i < names.Count; i++)
                    if (names[i].Connection == connection && names[i].Database == database && names[i].Collection == collection && value.SequenceEqual(names[i].Index))
                        return SyntaxTokenType.Index;
            return fallback;
        }
    }
}
