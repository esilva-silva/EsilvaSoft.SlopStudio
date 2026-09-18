namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

public static class SymbolKindExtensions
{
    public static SymbolKinds ToFlag(this SymbolKind kind) => (SymbolKinds)(1 << (int)kind);
}
