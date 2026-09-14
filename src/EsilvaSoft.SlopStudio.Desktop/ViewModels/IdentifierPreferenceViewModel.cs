using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed record IdentifierModeChoice(IdentifierRepresentationMode Value, string Label);

public sealed record IdentifierPreviewRow(string Label, string Code, string Details);

/// <summary>Global identifier mode (Standard, ObjectId or UUID v4) with a preview adapted to the selected mode.</summary>
public sealed partial class IdentifierPreferenceViewModel : ObservableObject
{
    /// <summary>UUID v4 sample (version 4, RFC 4122 variant) for the UUID section of the preview.</summary>
    public static Guid SampleUuidV4 { get; } = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private readonly Func<UuidRepresentation> _uuid;
    private readonly Func<IdentifierRepresentationMode, Task>? _apply;
    private bool _loading;

    /// <param name="uuid">Current global UUID representation, used by the UUID section of the preview.</param>
    /// <param name="apply">Called on each user selection to apply and persist the mode.</param>
    public IdentifierPreferenceViewModel(Func<UuidRepresentation> uuid, Func<IdentifierRepresentationMode, Task>? apply = null)
    {
        _uuid = uuid; _apply = apply;
        foreach (var mode in IdentifierRepresentationService.All) Choices.Add(new(mode, IdentifierRepresentationService.ChoiceLabel(mode)));
        Load(IdentifierRepresentationMode.Standard);
    }

    public string Title { get; } = "Representação padrão de identificadores";
    public ObservableCollection<IdentifierModeChoice> Choices { get; } = [];
    public ObservableCollection<IdentifierPreviewRow> ObjectIdPreview { get; } = [];
    public ObservableCollection<IdentifierPreviewRow> UuidPreview { get; } = [];
    [ObservableProperty] private IdentifierModeChoice? _selectedChoice;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isObjectIdPreviewVisible;
    [ObservableProperty] private bool _isUuidPreviewVisible;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasError;
    public IdentifierRepresentationMode Mode => SelectedChoice?.Value ?? IdentifierRepresentationMode.Standard;
    public Task ApplyTask { get; private set; } = Task.CompletedTask;

    public void Load(IdentifierRepresentationMode mode)
    {
        _loading = true;
        SelectedChoice = Choices.FirstOrDefault(choice => choice.Value == mode) ?? Choices[0];
        _loading = false;
        Status = ""; HasError = false;
        RefreshPreview();
    }

    /// <summary>Rebuilds the preview; also called when the UUID representation changes.</summary>
    public void RefreshPreview()
    {
        var mode = Mode;
        Description = IdentifierRepresentationService.Description(mode);
        IsObjectIdPreviewVisible = mode is IdentifierRepresentationMode.Standard or IdentifierRepresentationMode.ObjectId;
        IsUuidPreviewVisible = mode is IdentifierRepresentationMode.Standard or IdentifierRepresentationMode.UuidV4;
        ObjectIdPreview.Clear();
        if (IsObjectIdPreviewVisible)
        {
            var hex = IdentifierRepresentationService.SampleObjectId;
            ObjectIdPreview.Add(new("ObjectId", IdentifierRepresentationService.FormatObjectId(hex), "Tipo BSON ObjectId · 12 bytes"));
            ObjectIdPreview.Add(new("Hex", hex, "24 dígitos hexadecimais"));
            ObjectIdPreview.Add(new("UUID equivalente", IdentifierRepresentationService.ObjectIdToUuid(hex).ToString("D"),
                "12 bytes do ObjectId + 4 bytes zero · representação alternativa: não é UUID v4 e não altera o ObjectId gravado"));
        }
        UuidPreview.Clear();
        if (IsUuidPreviewVisible)
        {
            var representation = _uuid();
            UuidPreview.Add(new("UUID v4", IdentifierRepresentationService.FormatUuid(SampleUuidV4, representation),
                $"{UuidCodec.DisplayName(representation)} · Binary subtype {UuidCodec.SubType(representation)} · novos UUIDs usam esta forma; as quatro formas aparecem abaixo"));
        }
    }

    partial void OnSelectedChoiceChanged(IdentifierModeChoice? value)
    {
        OnPropertyChanged(nameof(Mode));
        if (_loading) return;
        RefreshPreview();
        if (_apply is not null && value is not null) ApplyTask = ApplyAsync(value.Value);
    }

    private async Task ApplyAsync(IdentifierRepresentationMode mode)
    {
        try
        {
            await _apply!(mode);
            HasError = false; Status = "Modo de identificador salvo. Resultados abertos foram atualizados; nenhum dado gravado foi alterado.";
        }
        catch (Exception exception)
        {
            HasError = true; Status = "Modo aplicado nesta sessão, mas não salvo: " + exception.Message;
        }
    }
}
