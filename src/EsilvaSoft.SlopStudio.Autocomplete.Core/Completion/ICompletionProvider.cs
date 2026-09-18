namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

public interface ICompletionProvider
{
    CompletionProviderKind Kind { get; }
    ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default);
}
