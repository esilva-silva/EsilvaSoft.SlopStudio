using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.SlopStudio.Infrastructure.LocalAi;

/// <summary>
/// Architecture contract of an ONNX GenAI export: structural validation, tokenizer, prompt format and stop tokens.
/// A new model family is added as an adapter instead of architecture checks spread through the application.
/// </summary>
public interface IModelAdapter
{
    string Architecture { get; }
    string PromptFormat { get; }
    /// <summary>Shown when no adapter accepts a folder.</summary>
    string Description { get; }
    LocalModelCapabilities DefaultCapabilities => LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Chat | LocalModelCapabilities.Fim;
    bool CanHandle(string modelType, string root);
    /// <summary>Returns a user-facing failure, or throws <see cref="InvalidDataException"/> for malformed files.</summary>
    ModelAdapterFailure? Validate(ModelFolder folder);
    ICompletionPromptBuilder CreatePromptBuilder();
    ITokenizer CreateTokenizer(Model model, string root);
    IReadOnlySet<int> GetStopTokens(ITokenizer tokenizer);
}
