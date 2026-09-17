namespace EsilvaSoft.SlopStudio.LocalAi.Core;

/// <summary>Captures the exact editor version used to create a proposal.</summary>
public sealed record AiChangeProposal(
    Guid Id,
    string OriginalContent,
    string ProposedContent,
    string Explanation,
    string Diff,
    bool RequiresAdditionalConfirmation,
    string Warning,
    AiEditorContext Context)
{
    public bool IsNoOp => string.Equals(OriginalContent, ProposedContent, StringComparison.Ordinal);
}
