using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>Read-only presentation of one result document. Built from memory only; it has no access to the workspace service.</summary>
public sealed partial class DocumentJsonViewModel(ResultDocumentViewModel document, double codeFontSize = 14) : ObservableObject
{
    public ResultDocumentViewModel Document => document;
    public string WindowTitle => document.Label + " · JSON";
    public string Heading => document.Label + " · " + document.Document.Set.Label;
    public string Origin => document.OriginText;
    public string Destination => document.DestinationText;
    public string Identity => document.IdentityText + (document.FlagsText.Length == 0 ? "" : " · " + document.FlagsText);
    public string Presentation => "Identificadores " + IdentifierRepresentationService.DisplayName(document.Options.Mode) + " · UUID " + UuidCodec.DisplayName(document.Representation)
        + (document.UnknownLegacyUuidCount == 0 ? "" : $" · {document.UnknownLegacyUuidCount} UUID(s) legado(s) de origem desconhecida") + " · exportação continua em Extended JSON canônico";
    public string Json => document.FormattedJson;
    public double CodeFontSize => codeFontSize;
    public double CodeLineHeight => codeFontSize * 1.5;
    [ObservableProperty] private string _status = "Visualização somente leitura; nada foi executado.";
}
