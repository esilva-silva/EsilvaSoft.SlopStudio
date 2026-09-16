namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>Character range of one document inside the JSON view text.</summary>
public sealed record ResultTextSegment(int Start, int Length, ResultDocumentViewModel Document);
