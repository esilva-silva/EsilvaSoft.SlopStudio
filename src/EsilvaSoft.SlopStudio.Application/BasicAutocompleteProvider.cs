using System.Text.RegularExpressions;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public static class BasicAutocompleteProvider
{
    private static readonly Regex Word = new(@"[\p{L}_$][\p{L}\p{N}_$]*", RegexOptions.CultureInvariant, CompletionPrivacy.MatchTimeout);
    private static readonly string[] Keywords = ["db", "getCollection", "getConnection", "ConnectionPool", "find", "findOne", "aggregate", "limit", "sort", "countDocuments", "insertOne", "updateOne", "deleteOne", "console", "const", "let", "function", "return", "ObjectId", "NumberLong", "NumberDecimal", "UUID", "CGUUID", "JUUID", "GUUID", "true", "false", "null"];

    public static AutocompleteResult? GetCompletion(AutocompleteRequest request)
    {
        var prefix = request.Prefix;
        var start = prefix.Length;
        while (start > 0 && (char.IsLetterOrDigit(prefix[start - 1]) || prefix[start - 1] is '_' or '$')) start--;
        var partial = prefix[start..];
        if (partial.Length == 0) return null;
        var words = CompletionPrivacy.ContainsSensitiveText(prefix + request.Suffix)
            ? Keywords.AsEnumerable()
            : Word.Matches(prefix[..start] + "\n" + request.Suffix).Select(m => m.Value).Concat(Keywords)
                .Concat(request.Dictionary.Where(word => !CompletionPrivacy.ContainsSensitiveText(word)));
        var candidate = words
            .Where(w => w.Length > partial.Length && w.StartsWith(partial, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).OrderBy(w => w.Length).ThenBy(w => w, StringComparer.Ordinal).FirstOrDefault();
        return candidate is null ? null : new(candidate[partial.Length..], false, "Autocomplete básico local");
    }

    private static string[] EditorWords(string text)
    {
        // Matches is lazy: enumerate here so a timeout degrades to keywords instead of failing the request.
        try { return Word.Matches(text).Select(m => m.Value).ToArray(); }
        catch (RegexMatchTimeoutException) { return []; }
    }
}

/// <summary>Conservative opt-out for recognizable secrets. No ENV expansion and no configuration access.</summary>
public static class CompletionPrivacy
{
    /// <summary>
    /// Wall-clock safety net for a scan of up to 65 536 characters. A 50 ms budget timed out on busy CI runners
    /// (GC and preemption count toward it), so the privacy pattern runs on the linear non-backtracking engine.
    /// </summary>
    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex Sensitive = new("mongodb(?:\\+srv)?://|(?:password|passwd|pwd|secret|access[_-]?token|api[_-]?key|authorization|connection[_-]?string)\\s*[\\\"']?\\s*[:=]|Bearer\\s+|-----BEGIN .*PRIVATE KEY-----",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, MatchTimeout);

    /// <summary>Fails closed: text that cannot be checked in time is treated as sensitive and is never sent to the model.</summary>
    public static bool ContainsSensitiveText(string text)
    {
        try { return Sensitive.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return true; }
    }
}
