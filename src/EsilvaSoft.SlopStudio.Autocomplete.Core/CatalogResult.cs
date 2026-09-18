namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

public sealed record CatalogResult(IReadOnlyList<CatalogCandidate> Candidates, CatalogCompleteness Completeness);
