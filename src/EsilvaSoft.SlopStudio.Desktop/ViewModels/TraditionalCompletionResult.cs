using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed record TraditionalCompletionResult(IReadOnlyList<CompletionItem> Items, bool IsIncomplete);
