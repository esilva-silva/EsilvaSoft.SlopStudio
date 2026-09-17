namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public sealed record LocalModelDefinition(string Id, string Name, string Path, string Architecture, string? TokenizerPath = null)
{
    public string PromptFormat { get; init; } = LocalModelPromptFormats.QwenFim;
    /// <summary>Without metadata a FIM model serves autocomplete and the chat proposal contract.</summary>
    public LocalModelCapabilities Capabilities { get; init; } = LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Chat | LocalModelCapabilities.Fim;
    public LocalModelMetadata? Metadata { get; init; }
}
