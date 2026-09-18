using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Context;

/// <summary>Immutable inputs captured on the editor thread before context analysis runs on a worker.</summary>
public sealed record ContextRequest(ITextSnapshot Snapshot, int Caret, EditorDialects Dialect, CatalogScope? TabScope,
    CompletionTrigger Trigger = CompletionTrigger.Automatic)
{
    public int ValidCaret => Math.Clamp(Caret, 0, Snapshot.Length);

    /// <summary>
    /// Forma do documento de entrada do pipeline, capturada sem I/O pelo chamador (cache local já carregado).
    /// Nula desativa a inferência de campos por estágio e mantém a lista ampla da coleção.
    /// </summary>
    public CollectionSchema? InputSchema { get; init; }
}
