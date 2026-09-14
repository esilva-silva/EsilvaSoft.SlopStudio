namespace EsilvaSoft.SlopStudio.Core;

public sealed record DocumentMutationResult(long MatchedCount, long ModifiedCount, string? InsertedId = null);
