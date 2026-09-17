using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

public sealed class CompletionRanker
{
    private readonly RankingProfile _profile;

    public CompletionRanker(RankingProfile? profile = null) => _profile = profile ?? new();

    public IReadOnlyList<CompletionItem> Rank(IEnumerable<CompletionItem> items, string prefix, int maximum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);

        // Do not preallocate maximum: a lazy sequence can have a very large requested cap.
        // The list grows only to the number of retained candidates.
        var heap = new List<Candidate>(Math.Min(maximum, 16));
        var ordinal = 0L;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryMatch(item.FilterText, prefix, out var match, out var matchScore, out var highlights))
            {
                ordinal++;
                continue;
            }

            var score = matchScore + SourceScore(item.Source);
            if (item.Tags.HasFlag(CompletionItemTags.Deprecated)) score -= _profile.DeprecatedPenalty;
            if (item.Tags.HasFlag(CompletionItemTags.Stale)) score -= _profile.StalePenalty;

            // CompletionItem has no structured compatibility signal. LabelDetail is localized
            // presentation text and must not change rank.
            var candidate = new Candidate(item with { Score = score, Highlights = highlights }, match, score, ordinal++);
            if (heap.Count < maximum)
            {
                heap.Add(candidate);
                SiftUpWorstFirst(heap, heap.Count - 1);
            }
            else if (CompareForOutput(candidate, heap[0]) < 0)
            {
                heap[0] = candidate;
                SiftDownWorstFirst(heap, 0);
            }
        }

        heap.Sort(CompareForOutput);
        var result = new CompletionItem[heap.Count];
        for (var index = 0; index < heap.Count; index++) result[index] = heap[index].Item;
        return result;
    }

    private double SourceScore(CompletionSource source) => source switch
    {
        CompletionSource.Catalog => _profile.SourceCatalog,
        CompletionSource.Schema => _profile.SourceSchema,
        CompletionSource.Snippet => _profile.SourceSnippet,
        _ => 0
    };

    /// <summary>Compares candidates in their final, best-to-worst output order.</summary>
    private static int CompareForOutput(Candidate left, Candidate right)
    {
        var comparison = right.Score.CompareTo(left.Score);
        if (comparison != 0) return comparison;

        comparison = MatchOrder(left.Match).CompareTo(MatchOrder(right.Match));
        if (comparison != 0) return comparison;

        comparison = StringComparer.OrdinalIgnoreCase.Compare(left.Item.Label, right.Item.Label);
        if (comparison != 0) return comparison;

        comparison = StringComparer.Ordinal.Compare(left.Item.SymbolId, right.Item.SymbolId);
        return comparison != 0 ? comparison : left.Ordinal.CompareTo(right.Ordinal);
    }

    private static void SiftUpWorstFirst(List<Candidate> heap, int child)
    {
        while (child > 0)
        {
            var parent = (child - 1) / 2;
            if (!IsWorse(heap[child], heap[parent])) return;
            (heap[child], heap[parent]) = (heap[parent], heap[child]);
            child = parent;
        }
    }

    private static void SiftDownWorstFirst(List<Candidate> heap, int parent)
    {
        while (true)
        {
            var left = (parent * 2) + 1;
            if (left >= heap.Count) return;

            var worstChild = left;
            var right = left + 1;
            if (right < heap.Count && IsWorse(heap[right], heap[left])) worstChild = right;
            if (!IsWorse(heap[worstChild], heap[parent])) return;

            (heap[parent], heap[worstChild]) = (heap[worstChild], heap[parent]);
            parent = worstChild;
        }
    }

    private static bool IsWorse(Candidate left, Candidate right) => CompareForOutput(left, right) > 0;

    private static int MatchOrder(CatalogMatch match) => match switch
    {
        CatalogMatch.Any => 0,
        CatalogMatch.Prefix => 1,
        CatalogMatch.Humps => 2,
        _ => 3
    };

    private bool TryMatch(string value, string prefix, out CatalogMatch match, out double score, out IReadOnlyList<TextSpan> highlights)
    {
        match = CatalogMatch.Any;
        score = _profile.ExactMatch;
        highlights = [];
        if (prefix.Length == 0) return true;
        if (value.Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            highlights = OneHighlight(0, value.Length);
            return true;
        }
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            match = CatalogMatch.Prefix; score = _profile.PrefixMatch + prefix.Length;
            highlights = OneHighlight(0, prefix.Length);
            return true;
        }
        if (HasCamelHumps(value, prefix))
        {
            match = CatalogMatch.Humps; score = _profile.CamelHumpMatch + prefix.Length;
            highlights = CamelHighlights(value, prefix);
            return true;
        }
        var index = value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            match = CatalogMatch.Substring; score = _profile.SubstringMatch + prefix.Length;
            highlights = OneHighlight(index, prefix.Length);
            return true;
        }
        return false;
    }

    private static TextSpan[] OneHighlight(int start, int length) => [new(start, length)];

    private static bool HasCamelHumps(string value, string prefix)
    {
        var next = 0;
        for (var index = 0; index < value.Length && next < prefix.Length; index++)
        {
            if (IsCamelBoundary(value, index) && char.ToUpperInvariant(value[index]) == char.ToUpperInvariant(prefix[next]))
            {
                next++;
            }
        }
        return next == prefix.Length;
    }

    private static TextSpan[] CamelHighlights(string value, string prefix)
    {
        var highlights = new TextSpan[prefix.Length];
        var next = 0;
        for (var index = 0; index < value.Length && next < prefix.Length; index++)
        {
            if (IsCamelBoundary(value, index) && char.ToUpperInvariant(value[index]) == char.ToUpperInvariant(prefix[next]))
            {
                highlights[next++] = new TextSpan(index, 1);
            }
        }
        return highlights;
    }

    private static bool IsCamelBoundary(string value, int index) =>
        index == 0 || value[index - 1] is '_' or '-' or '.' || char.IsUpper(value[index]);

    private readonly record struct Candidate(CompletionItem Item, CatalogMatch Match, double Score, long Ordinal);
}
