using System.Collections.ObjectModel;

namespace EsilvaSoft.SlopStudio.Application.Language.Context;

/// <summary>Whether the fields advertised by a pipeline position are safe to use for completion.</summary>
public enum PipelineFieldState : byte
{
    Known,
    Unknown
}

/// <summary>
/// The already-tokenized form of a value relevant to pipeline field inference. Text is a decoded literal, never source
/// code; parsing source text belongs to the tolerant parser and is deliberately not performed here.
/// </summary>
public enum PipelineValueKind : byte
{
    Unknown,
    Include,
    Exclude,
    Expression,
    FieldReference,
    Accumulator,
    NumericAccumulator,
    Literal,
    Names
}

/// <summary>One semantic value supplied by a pipeline-stage tokenizer.</summary>
public sealed record PipelineStageValue(PipelineValueKind Kind, string? Text = null, IReadOnlyList<string>? Names = null)
{
    public static PipelineStageValue Include { get; } = new(PipelineValueKind.Include);
    public static PipelineStageValue Exclude { get; } = new(PipelineValueKind.Exclude);
    public static PipelineStageValue Expression { get; } = new(PipelineValueKind.Expression);
    public static PipelineStageValue Unknown { get; } = new(PipelineValueKind.Unknown);

    public static PipelineStageValue Literal(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new(PipelineValueKind.Literal, value);
    }

    public static PipelineStageValue FieldReference(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new(PipelineValueKind.FieldReference, path);
    }

    public static PipelineStageValue Accumulator(string name, bool numeric = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new(numeric ? PipelineValueKind.NumericAccumulator : PipelineValueKind.Accumulator, name);
    }

    public static PipelineStageValue LiteralNames(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Length == 0 || names.Any(string.IsNullOrEmpty)) throw new ArgumentException("At least one non-empty literal name is required.", nameof(names));
        return new(PipelineValueKind.Names, Names: Array.AsReadOnly(names.ToArray()));
    }
}

/// <summary>One literal property name and its tokenized value in a stage document.</summary>
public sealed record PipelineStageProperty
{
    public PipelineStageProperty(string name, PipelineStageValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public PipelineStageValue Value { get; }
}

/// <summary>
/// A single aggregation stage whose name, property names and scalar literals have already been decoded by the caller.
/// This contract intentionally does not accept JavaScript or JSON source text.
/// </summary>
public sealed class PipelineStage
{
    public PipelineStage(string name, params PipelineStageProperty[] properties)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Any(property => property is null)) throw new ArgumentException("Stage properties cannot contain null.", nameof(properties));
        Name = name;
        Properties = Array.AsReadOnly(properties.ToArray());
    }

    public string Name { get; }
    public IReadOnlyList<PipelineStageProperty> Properties { get; }

    public static PipelineStage Unset(params string[] names) => new("$unset", new PipelineStageProperty("$unset", PipelineStageValue.LiteralNames(names)));
    public static PipelineStage Count(string fieldName) => new("$count", new PipelineStageProperty("$count", PipelineStageValue.Literal(fieldName)));
}

/// <summary>A field safe to offer at the current pipeline position. Source is present only for unmodified catalog fields.</summary>
public sealed record PipelineField(string Path, FieldNode? Source, IReadOnlyCollection<string> Types, FieldTraits Traits)
{
    internal static PipelineField FromSource(FieldNode field) => new(field.Path, field, new ReadOnlyCollection<string>(field.Types.Keys.Order(StringComparer.Ordinal).ToArray()), field.Flags);
    internal static PipelineField Computed(string path, IEnumerable<string>? types = null, FieldTraits traits = FieldTraits.None) =>
        new(path, null, new ReadOnlyCollection<string>((types ?? ["unknown"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()), traits);
}

/// <summary>
/// Immutable, conservative field shape after zero or more aggregation stages. It performs no I/O and never parses
/// source text or regular expressions. Unknown means that prior fields must not be offered as certain.
/// </summary>
public sealed class PipelineInfo
{
    private const int MaximumFields = 512;
    private readonly IReadOnlyDictionary<string, PipelineField> _fields;

    private PipelineInfo(PipelineFieldState state, IDictionary<string, PipelineField>? fields, int stageIndex)
    {
        State = state;
        StageIndex = stageIndex;
        _fields = new ReadOnlyDictionary<string, PipelineField>(new Dictionary<string, PipelineField>(fields ?? new Dictionary<string, PipelineField>(StringComparer.Ordinal), StringComparer.Ordinal));
    }

    public PipelineFieldState State { get; }
    public bool IsKnown => State == PipelineFieldState.Known;
    public int StageIndex { get; }
    public IReadOnlyDictionary<string, PipelineField> Fields => _fields;

    public static PipelineInfo From(CollectionSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var fields = new Dictionary<string, PipelineField>(StringComparer.Ordinal);
        foreach (var field in schema.Descendants())
        {
            if (fields.Count >= MaximumFields) return Unknown;
            fields[field.Path] = PipelineField.FromSource(field);
        }
        return new(PipelineFieldState.Known, fields, 0);
    }

    public static PipelineInfo Unknown { get; } = new(PipelineFieldState.Unknown, null, 0);

    public PipelineInfo Apply(PipelineStage stage, CollectionSchema? lookupSchema = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (!IsKnown) return new(PipelineFieldState.Unknown, null, StageIndex + 1);

        var fields = new Dictionary<string, PipelineField>(_fields, StringComparer.Ordinal);
        var result = stage.Name switch
        {
            "$match" or "$sort" or "$limit" or "$skip" or "$sample" => fields,
            "$project" => Project(fields, stage.Properties),
            "$addFields" or "$set" => AddFields(fields, stage.Properties),
            "$unset" => Unset(fields, stage.Properties),
            "$group" => Group(stage.Properties),
            "$lookup" => Lookup(fields, stage.Properties, lookupSchema),
            "$count" => Count(stage.Properties),
            _ => null
        };
        return result is null || result.Count > MaximumFields
            ? new(PipelineFieldState.Unknown, null, StageIndex + 1)
            : new(PipelineFieldState.Known, result, StageIndex + 1);
    }

    public PipelineInfo Apply(IEnumerable<PipelineStage> stages, Func<PipelineStage, CollectionSchema?>? lookupSchemas = null)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var current = this;
        foreach (var stage in stages)
        {
            ArgumentNullException.ThrowIfNull(stage);
            current = current.Apply(stage, lookupSchemas?.Invoke(stage));
        }
        return current;
    }

    private static Dictionary<string, PipelineField>? Project(Dictionary<string, PipelineField> input, IReadOnlyList<PipelineStageProperty> properties)
    {
        if (properties.Any(property => property.Value.Kind == PipelineValueKind.Unknown)) return null;
        var includes = properties.Where(property => property.Name != "_id" && property.Value.Kind is PipelineValueKind.Include or PipelineValueKind.Expression or PipelineValueKind.FieldReference).ToArray();
        if (includes.Length > 0)
        {
            if (properties.Any(property => property.Name != "_id" && property.Value.Kind == PipelineValueKind.Exclude)) return null;
            var output = new Dictionary<string, PipelineField>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                if (property.Value.Kind == PipelineValueKind.Exclude) continue;
                if (property.Value.Kind == PipelineValueKind.Include) CopySubtree(input, output, property.Name, property.Name);
                else if (property.Value.Kind == PipelineValueKind.FieldReference) CopySubtree(input, output, property.Value.Text!, property.Name);
                else if (property.Value.Kind == PipelineValueKind.Expression) AddComputed(output, property.Name);
                else return null;
            }
            return output;
        }

        if (properties.All(property => property.Value.Kind == PipelineValueKind.Exclude))
        {
            foreach (var property in properties) RemoveSubtree(input, property.Name);
            return input;
        }
        return null;
    }

    private static Dictionary<string, PipelineField>? AddFields(Dictionary<string, PipelineField> input, IReadOnlyList<PipelineStageProperty> properties)
    {
        foreach (var property in properties)
        {
            if (property.Value.Kind is PipelineValueKind.Unknown or PipelineValueKind.Names or PipelineValueKind.Include or PipelineValueKind.Exclude) return null;
            RemoveSubtree(input, property.Name);
            AddComputed(input, property.Name);
        }
        return input;
    }

    private static Dictionary<string, PipelineField>? Unset(Dictionary<string, PipelineField> input, IReadOnlyList<PipelineStageProperty> properties)
    {
        if (properties.Count != 1 || properties[0].Value.Kind != PipelineValueKind.Names || properties[0].Value.Names is not { Count: > 0 } names) return null;
        foreach (var name in names) RemoveSubtree(input, name);
        return input;
    }

    private static Dictionary<string, PipelineField>? Group(IReadOnlyList<PipelineStageProperty> properties)
    {
        if (properties.Count == 0 || properties.Any(property => property.Name != "_id" && property.Value.Kind is not (PipelineValueKind.Accumulator or PipelineValueKind.NumericAccumulator))) return null;
        var output = new Dictionary<string, PipelineField>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (property.Name == "_id") AddComputed(output, "_id");
            else AddComputed(output, property.Name, property.Value.Kind == PipelineValueKind.NumericAccumulator ? ["number"] : null);
        }
        return output;
    }

    private static Dictionary<string, PipelineField>? Lookup(Dictionary<string, PipelineField> input, IReadOnlyList<PipelineStageProperty> properties, CollectionSchema? foreignSchema)
    {
        var alias = properties.SingleOrDefault(property => property.Name == "as")?.Value;
        if (alias is null) return input;
        if (alias.Kind != PipelineValueKind.Literal || string.IsNullOrEmpty(alias.Text)) return null;
        RemoveSubtree(input, alias.Text);
        input[alias.Text] = PipelineField.Computed(alias.Text, ["array"], FieldTraits.Array | FieldTraits.ArrayOfDocuments);
        if (foreignSchema is null) return input;
        foreach (var field in foreignSchema.Descendants())
        {
            var path = alias.Text + "." + field.Path;
            input[path] = new PipelineField(path, field, new ReadOnlyCollection<string>(field.Types.Keys.Order(StringComparer.Ordinal).ToArray()), field.Flags);
        }
        return input;
    }

    private static Dictionary<string, PipelineField>? Count(IReadOnlyList<PipelineStageProperty> properties)
    {
        if (properties.Count != 1 || properties[0].Value.Kind != PipelineValueKind.Literal || string.IsNullOrEmpty(properties[0].Value.Text)) return null;
        var name = properties[0].Value.Text!;
        return new(StringComparer.Ordinal) { [name] = PipelineField.Computed(name, ["number"]) };
    }

    private static void CopySubtree(IReadOnlyDictionary<string, PipelineField> source, Dictionary<string, PipelineField> destination, string sourcePath, string destinationPath)
    {
        foreach (var (path, field) in source)
        {
            if (path != sourcePath && !path.StartsWith(sourcePath + ".", StringComparison.Ordinal)) continue;
            var target = destinationPath + path[sourcePath.Length..];
            destination[target] = new PipelineField(target, field.Source, field.Types, field.Traits);
        }
    }

    private static void AddComputed(Dictionary<string, PipelineField> fields, string path, IEnumerable<string>? types = null, FieldTraits traits = FieldTraits.None)
    {
        var prefix = path.IndexOf('.', StringComparison.Ordinal);
        while (prefix >= 0)
        {
            var parent = path[..prefix];
            fields.TryAdd(parent, PipelineField.Computed(parent));
            prefix = path.IndexOf('.', prefix + 1);
        }
        fields[path] = PipelineField.Computed(path, types, traits);
    }

    private static void RemoveSubtree(Dictionary<string, PipelineField> fields, string path)
    {
        foreach (var name in fields.Keys.Where(name => name == path || name.StartsWith(path + ".", StringComparison.Ordinal)).ToArray()) fields.Remove(name);
    }
}
