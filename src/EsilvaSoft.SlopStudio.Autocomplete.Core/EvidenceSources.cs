namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Where the knowledge about a field comes from.</summary>
[Flags]
public enum EvidenceSources
{
    None = 0, Validator = 1, Index = 2, Results = 4, Sample = 8, History = 16,
    /// <summary>Campo derivado dos estágios de agregação anteriores ao caret, sem leitura de dados.</summary>
    Pipeline = 32
}
