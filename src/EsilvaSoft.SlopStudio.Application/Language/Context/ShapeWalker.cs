using EsilvaSoft.SlopStudio.Application.Language.Syntax;

namespace EsilvaSoft.SlopStudio.Application.Language.Context;

/// <summary>Data-driven shape information at the cursor. A missing shape is an explicit conservative fallback.</summary>
public sealed record ShapeWalkResult(string? ShapeId, SymbolKinds ExpectedKinds, string ParentPath = "")
{
    public static ShapeWalkResult Unknown { get; } = new(null, SymbolKinds.None);
}

/// <summary>
/// Locates the innermost catalogued MongoDB call and applies its parameter shape. This is deliberately non-evaluating:
/// malformed/dynamic expressions return <see cref="ShapeWalkResult.Unknown"/> rather than borrowing a neighbouring call.
/// </summary>
public static class ShapeWalker
{
    public static ShapeWalkResult Walk(string text, int caret, CompletionCursorRole role, LanguageDefinition? definition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, text.Length);
        definition ??= LanguageDefinition.Default;
        var tokens = new List<MongoToken>();
        MongoLexer.Tokenize(text.AsSpan(), tokens, cancellationToken: cancellationToken);
        var call = FindCall(tokens, text, caret);
        if (call.Method is null) return ShapeWalkResult.Unknown;
        var method = definition.Symbols.FirstOrDefault(symbol => symbol.Name == call.Method && symbol.Parameters.Count > call.Argument);
        if (method is null) return ShapeWalkResult.Unknown;
        var shape = method.Parameters[call.Argument];
        return Descend(shape, tokens, text, call.Open, caret, role, definition);
    }

    private static (string? Method, int Open, int Argument) FindCall(List<MongoToken> tokens, string text, int caret)
    {
        var stack = new Stack<int>();
        var best = -1;
        for (var i = 0; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            var value = Value(tokens, text, i);
            if (value == "(") stack.Push(i);
            else if (value == ")" && stack.Count > 0) stack.Pop();
        }
        if (stack.Count > 0) best = stack.Peek();
        if (best <= 0 || tokens[best - 1].Kind != MongoTokenKind.Identifier) return (null, -1, 0);
        var argument = 0;
        var depth = 0;
        for (var i = best + 1; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            var value = Value(tokens, text, i);
            if (value is "{" or "[" or "(") depth++;
            else if (value is "}" or "]" or ")") depth--;
            else if (value == "," && depth == 0) argument++;
        }
        return (Value(tokens, text, best - 1), best, argument);
    }

    private static ShapeWalkResult Descend(string initial, List<MongoToken> tokens, string text, int callOpen, int caret,
        CompletionCursorRole role, LanguageDefinition definition)
    {
        var shape = initial;
        var stack = new Stack<(string Shape, string? Key)>();
        stack.Push((shape, null));
        for (var i = callOpen + 1; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            var value = Value(tokens, text, i);
            if (value == "[" && definition.Shapes.TryGetValue(shape, out var array) && array.Element is { } element)
            { shape = element; stack.Push((shape, null)); continue; }
            if (value == "{")
            {
                if (definition.Shapes.TryGetValue(shape, out var valueShape))
                {
                    var objectValue = valueShape.Values.FirstOrDefault(definition.Shapes.ContainsKey);
                    if (objectValue is not null) shape = objectValue;
                }
                stack.Push((shape, null));
                continue;
            }
            if (value is "}" or "]") { if (stack.Count > 1) { stack.Pop(); shape = stack.Peek().Shape; } continue; }
            if ((tokens[i].Kind is MongoTokenKind.Identifier or MongoTokenKind.String) && i + 1 < tokens.Count && Value(tokens, text, i + 1) == ":")
            {
                var key = Unquote(value);
                if (definition.Shapes.TryGetValue(shape, out var objectShape))
                {
                    var rule = Match(objectShape, key);
                    if (rule?.Value is { } next) { shape = next; stack.Push((shape, key)); }
                }
            }
        }
        if (!definition.Shapes.TryGetValue(shape, out var current)) return new(shape, ExpectedForPrimitive(shape));
        var expected = role switch
        {
            CompletionCursorRole.PropertyKey or CompletionCursorRole.PropertyKeyString => KeyKinds(current),
            CompletionCursorRole.ArrayElement => current.Element is { } element ? ExpectedForShape(element, definition) : KeyKinds(current),
            CompletionCursorRole.PropertyValue => ValueKinds(current),
            _ => ExpectedForShape(shape, definition)
        };
        var parent = stack.Reverse().Select(frame => frame.Key).LastOrDefault(key => !string.IsNullOrEmpty(key)) ?? "";
        return new(shape, expected, parent);
    }

    private static ShapeKeyRule? Match(ShapeDefinition shape, string key) => shape.Keys.FirstOrDefault(rule =>
        rule.Rule == "Fixed" && rule.Names.Contains(key, StringComparer.Ordinal)) ??
        shape.Keys.FirstOrDefault(rule => rule.Rule == "Operator" && key.StartsWith('$')) ??
        shape.Keys.FirstOrDefault(rule => rule.Rule is "FieldPath" or "Dynamic");
    private static SymbolKinds KeyKinds(ShapeDefinition shape) => shape.Keys.Aggregate(SymbolKinds.None, (all, rule) => all |
        (rule.Rule == "FieldPath" ? SymbolKinds.Field : rule.Rule is "Operator" or "Exclusive" ? rule.Kinds.Aggregate(SymbolKinds.None, (k, kind) => k | kind.ToFlag()) : SymbolKinds.None));
    private static SymbolKinds ValueKinds(ShapeDefinition shape) => shape.Values.Aggregate(SymbolKinds.None, (all, value) => all | ExpectedForPrimitive(value));
    private static SymbolKinds ExpectedForShape(string shape, LanguageDefinition definition) => definition.Shapes.TryGetValue(shape, out var value) ? KeyKinds(value) : ExpectedForPrimitive(shape);
    private static SymbolKinds ExpectedForPrimitive(string value) => value switch
    {
        "FieldPathString" or "FieldReference" => SymbolKinds.Field,
        "CollectionNameString" => SymbolKinds.Collection,
        "DatabaseNameString" => SymbolKinds.Database,
        _ => SymbolKinds.BsonConstructor | SymbolKinds.Snippet
    };
    private static string Value(List<MongoToken> tokens, string text, int index) => index is >= 0 and < int.MaxValue && index < tokens.Count ? text.Substring(tokens[index].Start, tokens[index].Length) : "";
    private static string Unquote(string value) => value.Length >= 2 && value[0] is '\'' or '"' ? value[1..^1] : value;
}
