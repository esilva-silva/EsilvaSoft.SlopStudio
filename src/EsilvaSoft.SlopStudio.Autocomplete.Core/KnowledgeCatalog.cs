using System.Diagnostics;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Combines sources and consults only those that provide the requested kinds. Everything is answered from memory.</summary>
public sealed class KnowledgeCatalog : IKnowledgeCatalog
{
    private readonly ICatalogSource[] _sources;

    public KnowledgeCatalog(IEnumerable<ICatalogSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToArray();
    }

    public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = Stopwatch.GetTimestamp();
        var candidates = new List<CatalogCandidate>(Math.Min(Math.Max(query.MaximumCandidates, 0), 64));
        var completeness = CatalogCompleteness.Complete;
        foreach (var source in _sources)
        {
            if ((source.ProvidedKinds & query.Kinds) == 0) continue;
            if (candidates.Count >= query.MaximumCandidates) break;
            cancellationToken.ThrowIfCancellationRequested();
            var collected = source.Collect(query, candidates, cancellationToken);
            if (collected > completeness) completeness = collected;
        }
        AutocompleteMetrics.CatalogQueryDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("completeness", completeness.ToString()));
        return new(candidates, completeness);
    }
}
