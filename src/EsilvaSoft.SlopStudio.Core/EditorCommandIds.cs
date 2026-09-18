namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Stable editor command identifiers persisted in <see cref="EditorKeyBindings"/>.</summary>
public static class EditorCommandIds
{
    public const string CompletionShow = "editor.completion.show";
    /// <summary>Reserved for explicit AI completion; no handler is bound yet.</summary>
    public const string CompletionAi = "editor.completion.ai";
    public const string InlineAccept = "editor.inline.accept";
    public const string InlineDismiss = "editor.inline.dismiss";
    public static IReadOnlyList<string> All { get; } = [CompletionShow, CompletionAi, InlineAccept, InlineDismiss];
}
