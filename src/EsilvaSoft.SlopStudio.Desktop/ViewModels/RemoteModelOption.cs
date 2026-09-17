using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>A downloadable variant; installed means a folder with its name already exists in the models directory.</summary>
public sealed class RemoteModelOption(RemoteModelVariant variant, bool isInstalled, bool hardwareMissing = false)
{
    public RemoteModelVariant Variant { get; } = variant;
    public bool IsInstalled { get; } = isInstalled;

    /// <summary>The variant needs a GPU and the runtime reported none available on this machine.</summary>
    public bool HardwareMissing { get; } = hardwareMissing;

    public string Title => Variant.Title;

    public string Subtitle => string.Join(" · ", new[]
    {
        AutocompleteSettingsViewModel.FormatSize(Variant.SizeBytes), Variant.Hint,
        HardwareMissing ? "GPU não detectada nesta máquina" : null, IsInstalled ? "instalado" : null
    }.Where(part => !string.IsNullOrEmpty(part)));

    public string Display => $"{Title} · {AutocompleteSettingsViewModel.FormatSize(Variant.SizeBytes)}" + (IsInstalled ? " — instalado" : "");
    public override string ToString() => Display;
}
