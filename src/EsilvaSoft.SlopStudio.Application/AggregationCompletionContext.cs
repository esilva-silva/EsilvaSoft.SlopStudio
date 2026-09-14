namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Tolerant lexical context for incomplete JSON/MQL. Never evaluates editor text.</summary>
internal sealed record AggregationCompletionContext(bool StageKey, bool FieldReference, bool InComment, string? Stage)
{
    public static AggregationCompletionContext Read(string text)
    {
        var stack = new Stack<(char Kind, bool StageObject, string? Stage)>();
        var stageKey = false;
        var value = false;
        string? stage = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) continue;
            if (c == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            {
                var line = text[++i] == '/';
                var end = line ? text.IndexOf('\n', i + 1) : text.IndexOf("*/", i + 1, StringComparison.Ordinal);
                if (end < 0) return new(false, false, true, stage);
                i = end + (line ? 0 : 1); continue;
            }
            if (c is '\'' or '"' or '`')
            {
                var start = ++i;
                for (; i < text.Length; i++)
                {
                    if (text[i] == '\\') { i++; continue; }
                    if (text[i] == c) break;
                }
                if (i >= text.Length) return new(stageKey, value && start < text.Length && text[start] == '$', false, stage);
                if (stageKey) stage = text[start..i];
                continue;
            }
            if (c is '{' or '[' or '(')
            {
                var isStage = c == '{' && stack.TryPeek(out var parent) && parent.Kind == '[';
                stack.Push((c, isStage, stage));
                stageKey = isStage; value = false; continue;
            }
            if (c is '}' or ']' or ')')
            {
                if (stack.TryPop(out var parent)) stage = parent.Stage;
                stageKey = false; value = false; continue;
            }
            if (c == ':') { stageKey = false; value = true; continue; }
            if (c == ',') { stageKey = stack.TryPeek(out var parent) && parent.StageObject; value = false; continue; }
            if (c == '$' || char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i + 1 < text.Length && (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] is '$' or '_' or '.')) i++;
                if (stageKey) stage = text[start..(i + 1)];
            }
        }
        return new(stageKey, false, false, stage);
    }
}
