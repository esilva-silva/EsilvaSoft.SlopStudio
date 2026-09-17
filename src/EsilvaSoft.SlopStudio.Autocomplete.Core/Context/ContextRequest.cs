using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Context;

/// <summary>Immutable inputs captured on the editor thread before context analysis runs on a worker.</summary>
public sealed record ContextRequest(ITextSnapshot Snapshot, int Caret, EditorDialects Dialect, CatalogScope? TabScope,
    CompletionTrigger Trigger = CompletionTrigger.Automatic)
{
    public int ValidCaret => Math.Clamp(Caret, 0, Snapshot.Length);
}
