using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

public sealed record CompletionEdit(TextSpan InsertRange, TextSpan ReplaceRange, string NewText, bool IsSnippet = false);
