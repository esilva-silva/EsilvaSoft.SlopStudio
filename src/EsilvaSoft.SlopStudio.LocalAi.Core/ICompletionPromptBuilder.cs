namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public interface ICompletionPromptBuilder
{
    IReadOnlyList<int> Build(string prefix, string suffix, int contextTokens, ITokenizer tokenizer);
}
