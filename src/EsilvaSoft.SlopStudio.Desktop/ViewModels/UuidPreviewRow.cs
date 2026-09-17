using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed record UuidPreviewRow(UuidRepresentation Representation, string Code, string Details, bool IsSelected)
{
    public string Marker => IsSelected ? "Selecionada" : "";
}
