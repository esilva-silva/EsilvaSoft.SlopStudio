using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

/// <summary>Optional slopstudio-model.json. Every field is optional; the folder name remains the identity.</summary>
public sealed record LocalModelMetadata
{
    public const string FileName = "slopstudio-model.json";
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Architecture { get; init; }
    public string? Parameters { get; init; }
    public IReadOnlyList<string> Domain { get; init; } = [];
    /// <summary>Null when the file does not declare capabilities.</summary>
    public LocalModelCapabilities? Capabilities { get; init; }
    /// <summary>Hardware the export supports; null means no restriction declared.</summary>
    public IReadOnlyList<AiAccelerationMode>? Hardware { get; init; }
    public int? RecommendedContextTokens { get; init; }
    public int? RecommendedCompletionTokens { get; init; }
    public LocalModelGenerationDefaults Autocomplete { get; init; } = new();
    public LocalModelGenerationDefaults Chat { get; init; } = new();
}
