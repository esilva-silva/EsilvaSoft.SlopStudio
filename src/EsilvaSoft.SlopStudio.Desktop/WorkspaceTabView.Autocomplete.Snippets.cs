using EsilvaSoft.SlopStudio.Application.Language.Completion;
using EsilvaSoft.SlopStudio.Application.Language.Text;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>Tab-stop navigation through the placeholders of a just-inserted snippet.</summary>
public partial class WorkspaceTabView
{
    private void BeginSnippet(int insertionStart, IReadOnlyList<SnippetPlaceholder> placeholders)
    {
        var ordered = placeholders.OrderBy(placeholder => placeholder.Index == 0 ? int.MaxValue : placeholder.Index)
            .Select(placeholder => new TextSpan(insertionStart + placeholder.Span.Start, placeholder.Span.Length)).ToArray();
        if (ordered.Length == 0) return;
        _snippetSession = new SnippetSession(CodeEditor.Text ?? "", ordered, 0);
        SelectSnippetPlaceholder(_snippetSession);
    }

    private bool MoveSnippetPlaceholder(bool reverse)
    {
        var session = _snippetSession;
        if (session is null || !string.Equals(CodeEditor.Text, session.DocumentText, StringComparison.Ordinal)) { _snippetSession = null; return false; }
        var next = session.Current + (reverse ? -1 : 1);
        if (next < 0 || next >= session.Placeholders.Length) { _snippetSession = null; return false; }
        session.Current = next;
        SelectSnippetPlaceholder(session);
        return true;
    }

    private void SelectSnippetPlaceholder(SnippetSession session)
    {
        var span = session.Placeholders[session.Current];
        _acceptingCompletion = true;
        try
        {
            CodeEditor.SelectionStart = span.Start;
            CodeEditor.SelectionEnd = span.End;
            CodeEditor.CaretIndex = span.Start;
        }
        finally { _acceptingCompletion = false; }
        CodeEditor.Focus();
    }

    private sealed class SnippetSession(string documentText, TextSpan[] placeholders, int current)
    {
        public string DocumentText { get; } = documentText;
        public TextSpan[] Placeholders { get; } = placeholders;
        public int Current { get; set; } = current;
    }
}
