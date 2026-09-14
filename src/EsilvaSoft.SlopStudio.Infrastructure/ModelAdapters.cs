using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Files already read by the catalog for one candidate folder.</summary>
public sealed record ModelFolder(string Root, string ModelType, string DecoderPath, JsonElement Tokenizer);

public sealed record ModelAdapterFailure(LocalModelState State, string Message);

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

public static class ModelAdapters
{
    /// <summary>Order matters: a llama export is DeepSeek only when its manifest is present.</summary>
    public static IReadOnlyList<IModelAdapter> Default { get; } = [new DeepSeekCoderModelAdapter(), new QwenCoderModelAdapter()];

    public static IModelAdapter For(LocalModelDefinition model, IReadOnlyList<IModelAdapter>? adapters = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return (adapters ?? Default).FirstOrDefault(adapter => adapter.Architecture == model.Architecture && adapter.PromptFormat == model.PromptFormat)
            ?? throw new NotSupportedException("Arquitetura não suportada.");
    }
}

public sealed class QwenCoderModelAdapter : IModelAdapter
{
    public string Architecture => "Qwen2.5-Coder";
    public string PromptFormat => LocalModelPromptFormats.QwenFim;
    public string Description => "Qwen2.5-Coder FIM (qwen2)";
    public bool CanHandle(string modelType, string root) => modelType == "qwen2";

    public ModelAdapterFailure? Validate(ModelFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var tokens = folder.Tokenizer.GetProperty("added_tokens").EnumerateArray()
            .Select(token => token.GetProperty("content").GetString()).ToHashSet(StringComparer.Ordinal);
        return QwenFimPromptBuilder.SpecialTokens.All(tokens.Contains) ? null
            : new(LocalModelState.Unsupported, "Tokenizer sem FIM do Qwen Coder. Autocomplete básico ativo.");
    }

    public ICompletionPromptBuilder CreatePromptBuilder() => new QwenFimPromptBuilder();
    public ITokenizer CreateTokenizer(Model model, string root) => new OnnxModelTokenizer(model);

    public IReadOnlySet<int> GetStopTokens(ITokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        return QwenFimPromptBuilder.SpecialTokens.Concat(["<|endoftext|>", "<|im_end|>", "<|fim_pad|>"])
            .Select(tokenizer.Encode).Where(ids => ids.Count == 1).Select(ids => ids[0]).ToHashSet();
    }
}

public sealed class DeepSeekCoderModelAdapter : IModelAdapter
{
    public const string ManifestFileName = "slopcoder_manifest.json";
    public string Architecture => "DeepSeek-Coder";
    public string PromptFormat => LocalModelPromptFormats.DeepSeekCoderFim;
    public string Description => "DeepSeek-Coder FIM (llama com slopcoder_manifest.json)";
    public bool CanHandle(string modelType, string root) => modelType == "llama" && File.Exists(Path.Combine(root, ManifestFileName));

    public ModelAdapterFailure? Validate(ModelFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        using var manifest = LocalModelCatalog.ReadJson(Path.Combine(folder.Root, ManifestFileName));
        var prompt = manifest.RootElement.GetProperty("prompt");
        if (prompt.GetProperty("format").GetString() != LocalModelPromptFormats.DeepSeekCoderFim) throw new InvalidDataException();
        var added = folder.Tokenizer.GetProperty("added_tokens").EnumerateArray()
            .ToDictionary(token => token.GetProperty("content").GetString()!, token => token.GetProperty("id").GetInt32(), StringComparer.Ordinal);
        foreach (var (key, expected) in DeepSeekFimPromptBuilder.Tokens)
            if (prompt.GetProperty("tokens").GetProperty(key).GetString() != expected.Text
                || prompt.GetProperty("token_ids").GetProperty(key).GetInt32() != expected.Id
                || !added.TryGetValue(expected.Text, out var id) || id != expected.Id) throw new InvalidDataException();
        var stops = manifest.RootElement.GetProperty("generation").GetProperty("stop_token_ids").EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
        if (!stops.SetEquals(StopIds())) throw new InvalidDataException();
        var data = folder.DecoderPath + ".data";
        return File.Exists(data) && new FileInfo(data).Length > 0 ? null
            : new(LocalModelState.MissingFiles, $"Arquivos ausentes: {Path.GetFileName(data)} (pesos externos do decoder).");
    }

    public ICompletionPromptBuilder CreatePromptBuilder() => new DeepSeekFimPromptBuilder();
    public ITokenizer CreateTokenizer(Model model, string root) => new DeepSeekModelTokenizer(Path.Combine(root, "tokenizer.json"));
    public IReadOnlySet<int> GetStopTokens(ITokenizer tokenizer) => StopIds().ToHashSet();
    private static IEnumerable<int> StopIds() => DeepSeekFimPromptBuilder.Tokens.Where(token => token.Key != "bos").Select(token => token.Value.Id);
}

internal sealed class OnnxModelTokenizer(Model model) : ITokenizer, IDisposable
{
    private readonly Tokenizer _native = new(model);
    public IReadOnlyList<int> Encode(string text) { using var sequences = _native.Encode(text); return sequences[0].ToArray(); }
    public string Decode(IEnumerable<int> tokens) => _native.Decode(tokens.ToArray());
    public void Dispose() => _native.Dispose();
}
