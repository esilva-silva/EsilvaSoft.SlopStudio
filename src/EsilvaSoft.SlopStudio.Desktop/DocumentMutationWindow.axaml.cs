using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class DocumentMutationWindow : Window
{
    public DocumentMutationWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, RoutingStrategies.Tunnel);
    }
    private async void Apply(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DocumentMutationViewModel { IsRunning: false } model) return;
        if (await Dialogs.ChooseAsync(this, model.Operation + " documento", model.Context + "\n" + model.Identity + "\nConfirmar esta operação?", "Confirmar", "Cancelar") != "Confirmar") return;
        try { await model.ExecuteConfirmedAsync(); }
        catch (Exception ex) { model.Status = "Falha: " + ex.Message; }
    }
    private void CancelOperation(object? sender, RoutedEventArgs e) => (DataContext as DocumentMutationViewModel)?.Cancel();
    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is DocumentMutationViewModel { IsRunning: true } model) { e.Cancel = true; model.Status = "Cancele a operação e aguarde antes de fechar."; }
        base.OnClosing(e);
    }
}
