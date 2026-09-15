namespace EsilvaSoft.SlopStudio.Core;

public enum AutocompleteMode { Automatic, Basic, Ai }
public enum AiAccelerationMode { Auto, Cpu, Gpu, Npu }
public enum AiExecutionProvider { Auto, Cpu, DirectML, Cuda, OpenVino, Qnn }
public enum LocalModelState { NotInstalled, Available, Loading, Ready, Invalid, Unsupported, Failed, MissingFiles, NotLoaded }

public sealed record AutocompleteSettings
{
    public int Version { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public AutocompleteMode Mode { get; init; } = AutocompleteMode.Automatic;
    /// <summary>External model folder, used only when <see cref="SelectedModel"/> is empty.</summary>
    public string ModelPath { get; init; } = "";
    /// <summary>Additive to version 1: directory scanned for models; empty uses the default local directory.</summary>
    public string ModelDirectory { get; init; } = "";
    /// <summary>Additive to version 1: folder name inside the models directory, never an absolute path.</summary>
    public string SelectedModel { get; init; } = "";
    /// <summary>Additive to version 1: folder name of a separate chat model; empty reuses <see cref="SelectedModel"/>.</summary>
    public string ChatModel { get; init; } = "";
    /// <summary>Additive to version 1: the chat assistant may use the local model.</summary>
    public bool ChatEnabled { get; init; } = true;
    public AiAccelerationMode Acceleration { get; init; } = AiAccelerationMode.Auto;
    public AiExecutionProvider ExecutionProvider { get; init; } = AiExecutionProvider.Auto;
    public int ContextTokens { get; init; } = 2048;
    public int MaximumCompletionTokens { get; init; } = 32;
    public int DelayMilliseconds { get; init; } = 150;
    public bool UseDictionary { get; init; } = true;
    public bool UseInputPanelContext { get; init; } = true;
    public bool UseResultPanelContext { get; init; } = true;
    public bool UseEditorContext { get; init; } = true;
    public bool IncrementalTab { get; init; } = true;
    /// <summary>Additive to version 1: the contextual list opens by itself on trigger characters; absent keeps it explicit only.</summary>
    public bool CompletionAutoOpenOnTrigger { get; init; }
    /// <summary>Additive to version 1: Enter accepts the highlighted list item; false leaves Enter to insert a new line.</summary>
    public bool CompletionEnterAccepts { get; init; } = true;

    public AutocompleteSettings Validate()
    {
        if (Version != 1 || !Enum.IsDefined(Mode) || !Enum.IsDefined(Acceleration) || !Enum.IsDefined(ExecutionProvider)
            || ModelPath is null || ModelPath.Length > 4096 || ModelDirectory is null || ModelDirectory.Length > 4096
            || !IsModelFolderName(SelectedModel) || !IsModelFolderName(ChatModel) || ContextTokens is < 64 or > 8192
            || MaximumCompletionTokens is < 1 or > 256 || DelayMilliseconds is < 50 or > 2000)
            throw new ArgumentException("Configuração de autocomplete inválida ou de versão não suportada.");
        return this;
    }

    /// <summary>Folder name of the model used by <paramref name="role"/>; empty when the external path applies.</summary>
    public string ModelReference(LocalModelRole role) => role == LocalModelRole.Chat && ChatModel.Length > 0 ? ChatModel : SelectedModel;

    public bool HasModelSelection(LocalModelRole role = LocalModelRole.Autocomplete) =>
        ModelReference(role).Length > 0 || !string.IsNullOrWhiteSpace(ModelPath);

    /// <summary>Selected folder inside the models directory, or the external folder; empty without a selection.</summary>
    public string ResolveModelPath(LocalModelRole role, string defaultDirectory)
    {
        var name = ModelReference(role);
        if (name.Length == 0) return ModelPath.Trim();
        var directory = string.IsNullOrWhiteSpace(ModelDirectory) ? defaultDirectory : ModelDirectory.Trim();
        return Path.Combine(directory, name);
    }

    /// <summary>A single folder segment: selections stay relative to the models directory and cannot escape it.</summary>
    public static bool IsModelFolderName(string? value) =>
        value is not null && (value.Length == 0 || value.Length <= 255 && value.Trim() == value && value is not ("." or "..")
            && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && value.IndexOfAny(['/', '\\', ':']) < 0);
}

/// <summary>Immutable bounded editor snapshot; auxiliary context is transient and never persisted.</summary>
public sealed record AutocompleteRequest(string Prefix, string Suffix, string? Language = null, string? FileName = null)
{
    public bool RequireComplete { get; init; }
    public string Context { get; init; } = "";
    public IReadOnlyList<string> Dictionary { get; init; } = [];
    public const int MaximumContextCharacters = 32768;
    public AutocompleteRequest Bounded() => this with
    {
        Prefix = Prefix.Length > MaximumContextCharacters ? Prefix[^MaximumContextCharacters..] : Prefix,
        Suffix = Suffix.Length > MaximumContextCharacters ? Suffix[..MaximumContextCharacters] : Suffix,
        Context = Context[..Math.Min(Context.Length, 8192)],
        Dictionary = Dictionary.Take(256).Select(word => word[..Math.Min(word.Length, 128)]).ToArray()
    };
}

/// <summary>Insertion at the captured cursor, without replacing the prefix.</summary>
public sealed record AutocompleteResult(string Text, bool IsAi, string Description);

public sealed record LocalModelDefinition(string Id, string Name, string Path, string Architecture, string? TokenizerPath = null)
{
    public string PromptFormat { get; init; } = LocalModelPromptFormats.QwenFim;
    /// <summary>Without metadata a FIM model serves autocomplete and the chat proposal contract.</summary>
    public LocalModelCapabilities Capabilities { get; init; } = LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Chat | LocalModelCapabilities.Fim;
    public LocalModelMetadata? Metadata { get; init; }
}

public sealed record LocalModelStatus(LocalModelState State, string Message, string? Provider = null)
{
    public string? ModelName { get; init; }
    public AiAccelerationMode? RequestedHardware { get; init; }
    public AiAccelerationMode? Backend { get; init; }
    public string? Device { get; init; }
    public TimeSpan? LoadTime { get; init; }
    public long? ProcessMemoryBytes { get; init; }
    public TimeSpan? FirstToken { get; init; }
    public double? TokensPerSecond { get; init; }
    public bool UsedFallback { get; init; }
}

public sealed record LocalModelValidation(LocalModelDefinition? Model, LocalModelStatus Status)
{
    /// <summary>Folder that was checked, including invalid candidates without a definition.</summary>
    public string Path { get; init; } = Model?.Path ?? "";
    public LocalModelValidity Validity => Status.State switch
    {
        LocalModelState.Available or LocalModelState.Loading or LocalModelState.Ready => LocalModelValidity.Valid,
        LocalModelState.Unsupported => LocalModelValidity.Unsupported,
        LocalModelState.MissingFiles or LocalModelState.NotInstalled => LocalModelValidity.MissingFiles,
        _ => LocalModelValidity.Invalid
    };
}

public sealed record ModelGenerationRequest(string Prefix, string Suffix, int ContextTokens, int MaximumTokens, bool RequireFullContext = false)
{
    /// <summary>Zero keeps greedy decoding.</summary>
    public double Temperature { get; init; }
}

public sealed record ModelGenerationResult(string Text, int GeneratedTokens, TimeSpan Elapsed, string Provider, bool IsComplete = true, bool UsedCpuFallback = false)
{
    public TimeSpan? TimeToFirstToken { get; init; }
}
