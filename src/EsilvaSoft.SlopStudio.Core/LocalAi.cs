namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Consumer of a local model. Prepared for separate autocomplete and chat models.</summary>
public enum LocalModelRole { Autocomplete, Chat }

[Flags]
public enum LocalModelCapabilities { None = 0, Autocomplete = 1, Chat = 2, Fim = 4, Embeddings = 8 }

/// <summary>Structural result of a model folder check; only the model test proves inference.</summary>
public enum LocalModelValidity { Valid, Invalid, Unsupported, MissingFiles }

/// <summary>An explicit user action (chat, model test) enters the model queue before background autocomplete.</summary>
public enum AiRequestPriority { Background, Interactive }

public static class LocalModelPromptFormats
{
    public const string QwenFim = "qwen-fim";
    public const string DeepSeekCoderFim = "deepseek-coder-fim";
}

public sealed record LocalModelGenerationDefaults(int? MaximumTokens = null, double? Temperature = null);

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

/// <summary>A backend reported by the runtime of this build on this machine.</summary>
public sealed record AiHardwareDevice(AiAccelerationMode Kind, string Provider, string Name, bool IsAvailable)
{
    public long? MemoryBytes { get; init; }
    public string? Reason { get; init; }
}

public sealed record LocalModelRuntimeInfo(AiAccelerationMode Backend, string Provider, string? Device, TimeSpan LoadTime)
{
    public long? ProcessMemoryBytes { get; init; }
    public bool UsedFallback { get; init; }
}

public sealed record LocalModelTestStep(string Name, bool Succeeded, string Detail = "");

public sealed record LocalModelTestReport(bool Succeeded, string Message, IReadOnlyList<LocalModelTestStep> Steps)
{
    public string? ModelName { get; init; }
    public AiAccelerationMode? Hardware { get; init; }
    public AiAccelerationMode? Backend { get; init; }
    public string? Provider { get; init; }
    public string? Device { get; init; }
    public TimeSpan? LoadTime { get; init; }
    public TimeSpan? FirstToken { get; init; }
    public double? TokensPerSecond { get; init; }
    public int GeneratedTokens { get; init; }
    public bool UsedFallback { get; init; }
}
