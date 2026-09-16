using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _identifierSnippet = "Gere um identificador no modo e na representação UUID desta conexão.";

    [ObservableProperty]
    private string _identifierExtendedJsonSnippet = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdentifierLabel))]
    private UuidRepresentation _uuidRepresentation = UuidRepresentation.Standard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdentifierLabel))]
    private IdentifierRepresentationMode _identifierMode = IdentifierRepresentationMode.Standard;

    public string IdentifierLabel => $"Identificadores {IdentifierRepresentationService.DisplayName(IdentifierMode)} · UUID {UuidCodec.DisplayName(UuidRepresentation)} · subtype {UuidCodec.SubType(UuidRepresentation)} · Extended JSON canônico abaixo";

    [ObservableProperty]
    private string _identifierInput = "";

    [ObservableProperty]
    private string _identifierInterpretation = "Cole ObjectId(\"…\"), 24 dígitos hexadecimais, UUID(\"…\")/CGUUID/JUUID/GUUID ou um UUID.";

    /// <summary>ObjectId mode generates an ObjectId, UUID v4 mode a UUID v4 and Standard one of each; both lines write the same values.</summary>
    [RelayCommand]
    private void GenerateIdentifier()
    {
        var generated = IdentifierRepresentationService.Generate(new(IdentifierMode, UuidRepresentation));
        IdentifierSnippet = string.Join("\n", generated.Select(value => value.Script));
        IdentifierExtendedJsonSnippet = string.Join("\n", generated.Select(value => value.ExtendedJson));
        StatusMessage = $"Identificador gerado no modo {IdentifierRepresentationService.DisplayName(IdentifierMode)}; construtor e Extended JSON gravam os mesmos valores.";
    }

    /// <summary>Interprets pasted text with the central parser; wrappers and constructors keep their explicit BSON type.</summary>
    [RelayCommand]
    private void InterpretIdentifier()
    {
        try
        {
            var value = IdentifierRepresentationService.ParseIdentifier(IdentifierInput, new(IdentifierMode, UuidRepresentation));
            var kind = value.Kind switch
            {
                IdentifierKind.ObjectId => "ObjectId",
                IdentifierKind.Uuid => "UUID · Binary subtype " + (value.ExtendedJson.Contains("\"subType\":\"03\"", StringComparison.Ordinal) ? "3" : "4"),
                _ => "Binary subtype 3 · UUID legado de origem desconhecida"
            };
            var lines = new List<string>
            {
                "Tipo: " + kind + (value.IsExplicitType ? " (explícito)" : " (inferido do texto)"),
                "Valor: " + value.Text,
                "Extended JSON canônico: " + value.ExtendedJson
            };
            if (value.UuidEquivalent is { } uuid) lines.Add("UUID equivalente: " + uuid + " (representação alternativa; o ObjectId não é alterado)");
            lines.Add("Filtro: { _id: " + value.Text + " }");
            IdentifierInterpretation = string.Join("\n", lines);
        }
        catch (FormatException exception)
        {
            IdentifierInterpretation = exception.Message;
        }
    }
}
