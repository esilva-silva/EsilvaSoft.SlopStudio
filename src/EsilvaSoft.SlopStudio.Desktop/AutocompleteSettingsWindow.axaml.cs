using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class AutocompleteSettingsWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public AutocompleteSettingsWindow()
    {
        InitializeComponent();
        _statusTimer.Tick += (_, _) => (DataContext as AutocompleteSettingsViewModel)?.RefreshStatus();
        Opened += async (_, _) =>
        {
            EnableAutocomplete.Focus();
            _statusTimer.Start();
            // Opening the AI preferences rescans the models directory and asks the runtime which hardware exists.
            if (DataContext is AutocompleteSettingsViewModel model) await Task.WhenAll(model.OpenedAsync(), model.LoadRemoteModelsIfNeededAsync());
        };
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            if (DataContext is not AutocompleteSettingsViewModel model) return;
            // A model download keeps running after the dialog closes; the status bar shows and cancels it.
            model.TestCommand.Cancel(); model.RefreshModelsCommand.Cancel(); model.DetectHardwareCommand.Cancel(); model.LoadRemoteModelsCommand.Cancel();
        };
    }

    private async void ChooseModelsDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AutocompleteSettingsViewModel model) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Diretório de modelos", AllowMultiple = false });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
            model.ModelDirectory = path;
            await model.RefreshModelsCommand.ExecuteAsync(null);
        }
        catch (Exception) { model.OperationStatus = "Não foi possível abrir o seletor. Digite o caminho do diretório."; }
    }

    private async void ChooseExternalModel(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AutocompleteSettingsViewModel model) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta de um modelo ONNX GenAI", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) await model.SelectExternalModelAsync(path);
        }
        catch (Exception) { model.OperationStatus = "Não foi possível abrir o seletor de pasta."; }
    }

    private async void OpenModelsDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AutocompleteSettingsViewModel model || model.EnsureModelsDirectory() is not { } directory) return;
        try
        {
            if (!await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory))) model.OperationStatus = $"Não foi possível abrir a pasta. Caminho: {directory}";
        }
        catch (Exception) { model.OperationStatus = $"Não foi possível abrir a pasta. Caminho: {directory}"; }
    }

    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        base.OnKeyDown(e);
    }
}
