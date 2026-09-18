namespace EsilvaSoft.SlopStudio.LocalAi.Core;

/// <summary>Assistant output is a proposal only; it must not be executed or inserted implicitly.</summary>
public sealed record AiChatResponse(
    string Explanation,
    string ProposedCode,
    string Diff,
    bool RequiresAdditionalConfirmation = false,
    string Warning = "")
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Explanation) && string.IsNullOrWhiteSpace(ProposedCode);
}
