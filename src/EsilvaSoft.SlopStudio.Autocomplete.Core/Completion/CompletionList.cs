using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

public sealed record CompletionList(TextSnapshotVersion Version, IReadOnlyList<CompletionItem> Items, bool IsIncomplete)
{
    public static CompletionList Empty(TextSnapshotVersion version) => new(version, [], false);
}
