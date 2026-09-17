using System.Text.Json.Serialization;

namespace EsilvaSoft.SlopStudio.Core;

public sealed record WorkspacePreferences
{
    public AutocompleteSettings Autocomplete { get; init; } = new();
    public string Theme { get; init; } = "Sistema";
    public double CodeFontSize { get; init; } = 14;
    public double ExplorerWidth { get; init; } = 260;
    public double EditorRatio { get; init; } = 0.6;
    public bool RecoverDrafts { get; init; } = true;
    public Guid[] ExcludedProfileIds { get; init; } = [];
    /// <summary>Additive to version 1: sessions without this value keep Standard.</summary>
    public UuidRepresentation UuidRepresentation { get; init; } = UuidRepresentation.Standard;
    /// <summary>Per-connection overrides; a missing entry uses <see cref="UuidRepresentation"/>.</summary>
    public Dictionary<Guid, UuidRepresentation> ProfileUuidRepresentations { get; init; } = [];
    /// <summary>
    /// Additive to version 1 and independent of <see cref="UuidRepresentation"/>: sessions without this value use Standard
    /// (ObjectId + UUID) and keep their UUID representation unchanged.
    /// </summary>
    public IdentifierRepresentationMode IdentifierMode { get; init; } = IdentifierRepresentationMode.Standard;
    /// <summary>
    /// Additive to version 1: profiles whose autocomplete may sample field names and types without an explicit action.
    /// Sessions without this value never sample automatically.
    /// </summary>
    public Guid[] SchemaSamplingProfileIds { get; init; } = [];
    /// <summary>
    /// Additive to version 1: null (absent) uses <see cref="Core.EditorKeyBindings.Defaults"/> and is not written back,
    /// so sessions without custom shortcuts keep their shape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EditorKeyBindings? EditorKeyBindings { get; init; }

    public void ValidateKeyBindings() => EditorKeyBindings?.Validate();

    public void ValidateMetadata()
    {
        if (SchemaSamplingProfileIds is null || SchemaSamplingProfileIds.Contains(Guid.Empty))
            throw new InvalidDataException("Preferência de amostragem de schema inválida.");
    }

    public void ValidateUuid()
    {
        if (!Enum.IsDefined(UuidRepresentation) || ProfileUuidRepresentations is null
            || ProfileUuidRepresentations.Any(entry => entry.Key == Guid.Empty || !Enum.IsDefined(entry.Value)))
            throw new InvalidDataException("Preferência de UUID inválida ou de versão não suportada.");
        if (!Enum.IsDefined(IdentifierMode))
            throw new InvalidDataException("Modo de identificador inválido ou de versão não suportada.");
    }
}
