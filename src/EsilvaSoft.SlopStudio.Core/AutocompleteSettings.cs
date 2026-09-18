using System.Text.Json.Serialization;

namespace EsilvaSoft.SlopStudio.Core;

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

    // As três opções abaixo são aditivas à versão 1 e distinguem "campo ausente" de "false explícito" pela presença no
    // JSON, e não pelo inicializador da propriedade: o valor persistido é o anulável, e o valor efetivo é derivado.
    // Um documento antigo (sem as flags) precisa ser migrado; um documento novo com false explícito precisa ser
    // obedecido. Só o anulável é serializado, e apenas quando alguém realmente escolheu um valor.
    private readonly bool? _inlineEnabled;
    private readonly bool? _inlineUseTraditional;
    private readonly bool? _inlineUseAi;

    /// <summary>Valor persistido de <see cref="InlineEnabled"/>; nulo significa ausente no documento salvo.</summary>
    [JsonPropertyName(nameof(InlineEnabled)), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InlineEnabledValue { get => _inlineEnabled; init => _inlineEnabled = value; }

    /// <summary>Valor persistido de <see cref="InlineUseTraditional"/>; nulo significa ausente no documento salvo.</summary>
    [JsonPropertyName(nameof(InlineUseTraditional)), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InlineUseTraditionalValue { get => _inlineUseTraditional; init => _inlineUseTraditional = value; }

    /// <summary>Valor persistido de <see cref="InlineUseAi"/>; nulo significa ausente no documento salvo.</summary>
    [JsonPropertyName(nameof(InlineUseAi)), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InlineUseAiValue { get => _inlineUseAi; init => _inlineUseAi = value; }

    /// <summary>Additive to version 1: suspends every automatic (inline) suggestion; absent means enabled.</summary>
    [JsonIgnore]
    public bool InlineEnabled { get => _inlineEnabled ?? true; init => _inlineEnabled = value; }

    /// <summary>
    /// Additive to version 1: deterministic generator of the automatic suggestion. Absent derives from
    /// <see cref="UseDictionary"/>, which is the closest legacy intent for "automatic suggestion without AI"; a
    /// document that stored false explicitly keeps false even with the dictionary on.
    /// </summary>
    [JsonIgnore]
    public bool InlineUseTraditional { get => _inlineUseTraditional ?? UseDictionary; init => _inlineUseTraditional = value; }

    /// <summary>
    /// Additive to version 1: automatic (typing-triggered) AI inference. Absent means false — processamento automático
    /// de IA é opt-in explícito, tanto em instalação nova quanto em documento antigo migrado. A IA explícita
    /// (<c>Mode != Basic</c>) continua disponível e não depende desta opção.
    /// </summary>
    [JsonIgnore]
    public bool InlineUseAi { get => _inlineUseAi ?? false; init => _inlineUseAi = value; }

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
