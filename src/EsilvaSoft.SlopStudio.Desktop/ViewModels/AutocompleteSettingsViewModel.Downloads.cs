using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>A downloadable variant; installed means a folder with its name already exists in the models directory.</summary>
public sealed class RemoteModelOption(RemoteModelVariant variant, bool isInstalled)
{
    public RemoteModelVariant Variant { get; } = variant;
    public bool IsInstalled { get; } = isInstalled;
    public string Display => $"{Variant.Variant} · {AutocompleteSettingsViewModel.FormatSize(Variant.SizeBytes)}" + (IsInstalled ? " — instalado" : "");
    public override string ToString() => Display;
}

public sealed partial class AutocompleteSettingsViewModel
{
    public ObservableCollection<RemoteModelOption> RemoteModels { get; } = [];
    public bool HasRemoteSource => _remote is not null;
    public bool HasRemoteModelDetails => RemoteModelDetails.Length > 0;
    public bool HasDownloadStatus => DownloadStatus.Length > 0;
    public string DownloadPercent => $"{DownloadProgress:F0}%";
    public string RemoteSourceNotice => _remote is null ? ""
        : $"Fonte: {_remote.RepositoryUrl.Host}{_remote.RepositoryUrl.AbsolutePath}. Ao baixar, você aceita a licença publicada no repositório. "
          + "O download continua se esta janela for fechada; acompanhe ou cancele na barra inferior.";

    public string RemoteModelDetails => SelectedRemoteModel is not { } option ? ""
        : $"{option.Variant.FolderName} · {FormatSize(option.Variant.SizeBytes)} · licença {option.Variant.License ?? "não informada"} · "
          + (option.IsInstalled ? "já instalado em " : "instala em ") + Path.Combine(EffectiveModelDirectory, option.Variant.FolderName);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RemoteModelDetails), nameof(HasRemoteModelDetails)), NotifyCanExecuteChangedFor(nameof(DownloadModelCommand))]
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
            ShowRemoteModels(variants, SelectedRemoteModel?.Variant.Variant);
            RemoteModelsPlaceholder = variants.Count == 0 ? "Nenhum modelo publicado no repositório" : "Escolha um modelo para baixar";
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
        DownloadStatus = $"Baixando {variant.FolderName} para {directory}…";
        using var operation = _operations?.Begin($"Baixando modelo {variant.FolderName}", ApplicationOperationPriority.Normal, canCancel: true, cancellationToken);
        var token = operation?.Token ?? cancellationToken;
        var progress = new Progress<RemoteModelProgress>(update =>
        {
            if (!IsDownloadingModel) return;
            DownloadProgress = update.TotalBytes > 0 ? Math.Clamp(100d * update.CompletedBytes / update.TotalBytes, 0, 100) : 0;
            DownloadStatus = $"Baixando {variant.FolderName}: {FormatSize(update.CompletedBytes)} de {FormatSize(update.TotalBytes)}.";
            operation?.Report(update.CompletedBytes, update.TotalBytes);
        });
        try
        {
            // Hashing gigabytes stays off the UI thread; Progress<T> marshals updates back.
            var path = await Task.Run(() => remote.DownloadAsync(variant, directory, progress, token), CancellationToken.None);
            operation?.Complete(ApplicationOperationStatus.Success, $"Modelo {variant.FolderName} baixado");
            IsDownloadingModel = false;
            DownloadProgress = 100;
            await RefreshModelsCommand.ExecuteAsync(null);
            if (Models.FirstOrDefault(model => !model.IsExternal && string.Equals(model.Reference, variant.FolderName, PathComparison)) is { } installed)
            {
                SelectedModelOption = installed;
                DownloadStatus = $"Modelo instalado em {path} e selecionado. Clique em Salvar para usá-lo.";
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
        if (RemoteModels.Count > 0) ShowRemoteModels(RemoteModels.Select(option => option.Variant).ToArray(), SelectedRemoteModel?.Variant.Variant);
    }

    private void ShowRemoteModels(IEnumerable<RemoteModelVariant> variants, string? selectedVariant)
    {
        var directory = EffectiveModelDirectory;
        var options = variants.Select(variant => new RemoteModelOption(variant, IsInstalled(directory, variant))).ToArray();
        RemoteModels.Clear();
        foreach (var option in options) RemoteModels.Add(option);
        SelectedRemoteModel = options.FirstOrDefault(option => option.Variant.Variant == selectedVariant)
            ?? options.FirstOrDefault(option => !option.IsInstalled) ?? options.FirstOrDefault();
    }

    private static bool IsInstalled(string directory, RemoteModelVariant variant)
    {
        if (directory.Length == 0) return false;
        try { return Directory.Exists(Path.Combine(directory, variant.FolderName)); }
        catch (ArgumentException) { return false; }
    }
}
