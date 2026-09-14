namespace EsilvaSoft.SlopStudio.Core;

public sealed record DistinctValuesResult(IReadOnlyList<string> Values, bool IsTruncated, TimeSpan Duration);
