namespace EsilvaSoft.SlopStudio.Application.Language;

/// <summary>Sampled field with its BSON type name and nested structure, as returned by the server-side key/type pipeline.</summary>
public sealed record SampledField(string Name, string Type, IReadOnlyList<SampledField> Children, IReadOnlyList<SampledElement> Elements);

public sealed record SampledElement(string Type, IReadOnlyList<SampledField> Children);

public sealed record SampledDocument(IReadOnlyList<SampledField> Fields);
