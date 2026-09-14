namespace EsilvaSoft.SlopStudio.Core;

public sealed record QueryPage(
    IReadOnlyList<string> Documents,
    TimeSpan Duration,
    bool IsTruncated);
