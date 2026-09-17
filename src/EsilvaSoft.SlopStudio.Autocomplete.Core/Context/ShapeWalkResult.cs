namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Context;

/// <summary>Data-driven shape information at the cursor. A missing shape is an explicit conservative fallback.</summary>
public sealed record ShapeWalkResult(string? ShapeId, SymbolKinds ExpectedKinds, string ParentPath = "")
{
    public static ShapeWalkResult Unknown { get; } = new(null, SymbolKinds.None);
}
