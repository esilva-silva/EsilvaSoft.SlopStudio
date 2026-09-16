using EsilvaSoft.SlopStudio.Application.Language.Completion;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed record TraditionalCompletionResult(IReadOnlyList<CompletionItem> Items, bool IsIncomplete);
