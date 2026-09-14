using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
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

public sealed partial class AutocompleteSettingsViewModel
{
    public ObservableCollection<RemoteModelOption> RemoteModels { get; } = [];
    public bool HasRemoteSource => _remote is not null;
    public bool HasSelectedRemoteModel => SelectedRemoteModel is not null;
    public bool HasRemoteModelDetails => RemoteModelDetails.Length > 0;
    public bool HasDownloadStatus => DownloadStatus.Length > 0;
    public string DownloadPercent => $"{DownloadProgress:F0}%";

    /// <summary>Card of the source model, falling back to the folder of the export.</summary>
    public Uri? SelectedRemoteModelCardUrl => SelectedRemoteModel?.Variant.BaseModelUrl ?? SelectedRemoteModel?.Variant.PageUrl;

    public string RemoteSourceNotice => _remote is null ? ""
        : "Fonte: Hugging Face (" + string.Join(", ", _remote.RepositoryUrls.Select(url => url.AbsolutePath.Trim('/'))) + "), versões ONNX dos modelos SlopCoder-Mongo. "
          + "Ao baixar, você aceita a licença publicada no repositório. O download continua se esta janela for fechada; acompanhe ou cancele na barra inferior.";

    public string RemoteModelDetails => SelectedRemoteModel is not { } option ? ""
        : $"Licença {option.Variant.License ?? "não informada"} · " + (option.IsInstalled ? "já instalado em " : "instala em ")
          + Path.Combine(EffectiveModelDirectory, option.Variant.FolderName);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RemoteModelDetails), nameof(HasRemoteModelDetails), nameof(HasSelectedRemoteModel), nameof(SelectedRemoteModelCardUrl))]
    [NotifyCanExecuteChangedFor(nameof(DownloadModelCommand))]
    private RemoteModelOption? _selectedRemoteModel;
    [ObservableProperty] private string _remoteModelsPlaceholder = "Lista de modelos não carregada";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(DownloadModelCommand))] private bool _isDownloadingModel;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DownloadPercent))] private double _downloadProgress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDownloadStatus))] private string _downloadStatus = "";

    partial void OnModelDirectoryChanged(string value) => RefreshInstalledRemoteModels();

    /// <summary>Fetches the published list the first time the window opens; later openings only recheck what is installed.</summary>
    public Task LoadRemoteModelsIfNeededAsync()
    {
        if (_remote is null || IsDownloadingModel) return Task.CompletedTask;
        if (RemoteModels.Count == 0) return LoadRemoteModelsCommand.ExecuteAsync(null);
        RefreshInstalledRemoteModels();
        return Task.CompletedTask;
    }

    /// <summary>Creates the effective models directory when missing, so the file manager can open it.</summary>
    public string? EnsureModelsDirectory()
    {
        var directory = EffectiveModelDirectory;
        if (directory.Length == 0)
        {
            OperationStatus = "Diretório de modelos não definido.";
            return null;
        }
        try { return Directory.CreateDirectory(directory).FullName; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            OperationStatus = $"Não foi possível criar {directory}: {ex.Message}";
            return null;
        }
    }

    internal static string FormatSize(long bytes) => bytes >= 1_000_000_000
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes / 1_000_000_000d:0.##} GB")
        : string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, Math.Round(bytes / 1_000_000d)):0} MB");

    [RelayCommand]
    private async Task LoadRemoteModelsAsync(CancellationToken cancellationToken)
    {
        if (_remote is null) return;
        RemoteModelsPlaceholder = "Carregando modelos do Hugging Face…";
        try
        {
            var variants = await _remote.ListAsync(cancellationToken);
            ShowRemoteModels(variants, SelectedRemoteModel?.Variant);
            RemoteModelsPlaceholder = variants.Count == 0 ? "Nenhum modelo publicado nos repositórios" : "Escolha um modelo para baixar";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { RemoteModelsPlaceholder = "Lista de modelos não carregada"; }
        catch (Exception) { RemoteModelsPlaceholder = "Lista indisponível. Confira a conexão e use Atualizar lista."; }
    }

    private bool CanDownloadModel() => _remote is not null && SelectedRemoteModel is { IsInstalled: false } && !IsDownloadingModel;

    [RelayCommand(CanExecute = nameof(CanDownloadModel), IncludeCancelCommand = true)]
    private async Task DownloadModelAsync(CancellationToken cancellationToken)
    {
        if (_remote is not { } remote || SelectedRemoteModel is not { IsInstalled: false } option) return;
        var variant = option.Variant;
        var directory = EffectiveModelDirectory;
        IsDownloadingModel = true;
        DownloadProgress = 0;
        DownloadStatus = $"Baixando {variant.Title} para {directory}…";
        using var operation = _operations?.Begin($"Baixando modelo {variant.Title}", ApplicationOperationPriority.Normal, canCancel: true, cancellationToken);
        var token = operation?.Token ?? cancellationToken;
        var progress = new Progress<RemoteModelProgress>(update =>
        {
            if (!IsDownloadingModel) return;
            DownloadProgress = update.TotalBytes > 0 ? Math.Clamp(100d * update.CompletedBytes / update.TotalBytes, 0, 100) : 0;
            DownloadStatus = $"Baixando {variant.Title}: {FormatSize(update.CompletedBytes)} de {FormatSize(update.TotalBytes)}.";
            operation?.Report(update.CompletedBytes, update.TotalBytes);
        });
        try
        {
            // Hashing gigabytes stays off the UI thread; Progress<T> marshals updates back.
            var path = await Task.Run(() => remote.DownloadAsync(variant, directory, progress, token), CancellationToken.None);
            operation?.Complete(ApplicationOperationStatus.Success, $"Modelo {variant.Title} baixado");
            IsDownloadingModel = false;
            DownloadProgress = 100;
            await RefreshModelsCommand.ExecuteAsync(null);
            if (Models.FirstOrDefault(model => !model.IsExternal && string.Equals(model.Reference, variant.FolderName, PathComparison)) is { } installed)
            {
                SelectedModelOption = installed;
                DownloadStatus = $"{variant.Title} instalado em {path} e selecionado. Clique em Salvar para usá-lo.";
            }
            else DownloadStatus = $"Modelo baixado em {path}, mas o catálogo não o reconheceu. Confira as pastas ignoradas.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            operation?.Complete(ApplicationOperationStatus.Cancelled, "Download de modelo cancelado");
            DownloadStatus = "Download cancelado. Arquivos já verificados ficam guardados para retomar na próxima tentativa.";
        }
        catch (Exception ex)
        {
            operation?.Complete(ApplicationOperationStatus.Error, "Modelo não baixado: " + ex.Message);
            DownloadStatus = "Download não concluído: " + ex.Message;
        }
        finally
        {
            IsDownloadingModel = false;
            RefreshInstalledRemoteModels();
        }
    }

    private void RefreshInstalledRemoteModels()
    {
        if (RemoteModels.Count > 0) ShowRemoteModels(RemoteModels.Select(option => option.Variant).ToArray(), SelectedRemoteModel?.Variant);
    }

    private void ShowRemoteModels(IEnumerable<RemoteModelVariant> variants, RemoteModelVariant? selected)
    {
        var directory = EffectiveModelDirectory;
        // Only a completed detection that found no usable GPU marks GPU exports; an unknown state never warns.
        var gpuMissing = _hardware.Count > 0 && !_hardware.Any(device => device.Kind == AiAccelerationMode.Gpu && device.IsAvailable);
        var options = variants.Select(variant => new RemoteModelOption(variant, IsInstalled(directory, variant),
            gpuMissing && variant.Flavor?.Hardware == AiAccelerationMode.Gpu)).ToArray();
        RemoteModels.Clear();
        foreach (var option in options) RemoteModels.Add(option);
        SelectedRemoteModel = options.FirstOrDefault(option => selected is not null && option.Variant.FolderName == selected.FolderName)
            ?? options.FirstOrDefault(option => !option.IsInstalled && !option.HardwareMissing) ?? options.FirstOrDefault();
    }

    private static bool IsInstalled(string directory, RemoteModelVariant variant)
    {
        if (directory.Length == 0) return false;
        try { return Directory.Exists(Path.Combine(directory, variant.FolderName)); }
        catch (ArgumentException) { return false; }
    }
}
