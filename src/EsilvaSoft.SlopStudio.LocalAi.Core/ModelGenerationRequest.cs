namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public sealed record ModelGenerationRequest(string Prefix, string Suffix, int ContextTokens, int MaximumTokens, bool RequireFullContext = false)
{
    /// <summary>Zero keeps greedy decoding.</summary>
    public double Temperature { get; init; }
}
