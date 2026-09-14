using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class DocumentJsonWindow : Window
{
    public DocumentJsonWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, RoutingStrategies.Tunnel);
        Opened += (_, _) => JsonText.Focus();
    }

    /// <summary>Completes when the formatted JSON reached the clipboard or the failure is shown in the status line.</summary>
    public Task CopyTask { get; private set; } = Task.CompletedTask;

    private void CopyJson(object? sender, RoutedEventArgs e) => CopyTask = CopyAsync();

    private async Task CopyAsync()
    {
        if (DataContext is not DocumentJsonViewModel model) return;
        try
        {
            if (Clipboard is not { } clipboard) { model.Status = "Área de transferência indisponível."; return; }
            await clipboard.SetTextAsync(model.Json);
            model.Status = "JSON formatado copiado para a área de transferência.";
        }
        catch (Exception ex) { model.Status = "Não foi possível copiar: " + ex.Message; }
    }

    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();

}
