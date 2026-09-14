using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class HistoryWindow : Window
{
    public HistoryWindow() => InitializeComponent();
    private void OpenConsoleHistory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedConsoleHistory: { } entry } && Owner is MainWindow { WorkspaceModel: { } workspace })
        { workspace.OpenConsoleHistory(entry); Close(); }
    }
    private async Task<bool> CanReplaceAsync(WorkspaceTabViewModel tab) => !tab.IsRunning && (!tab.IsDirty || await Dialogs.ChooseAsync(this, "Substituir conteúdo", "Substituir o conteúdo alterado desta aba pela consulta escolhida?", "Substituir", "Cancelar") == "Substituir");
    private async void ApplyHistory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedHistory: not null } tab && await CanReplaceAsync(tab)) { tab.SelectedSavedQuery = null; tab.ApplyHistoryCommand.Execute(null); Close(); }
    }
    private async void ApplySavedQuery(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedSavedQuery: not null } tab && await CanReplaceAsync(tab)) { tab.SelectedHistory = null; tab.ApplyHistoryCommand.Execute(null); Close(); }
    }
    private async void OpenRecent(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel { SelectedScriptHistory: not null } tab || Owner is not MainWindow { WorkspaceModel: { } vm }) return;
        var entry = tab.SelectedScriptHistory;
        vm.NewTabCommand.Execute(null);
        try { await vm.ActiveTab!.OpenAsync(entry.Path); if (entry.InputJson is not null) vm.ActiveTab.InputJson = entry.InputJson; Close(); }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, "Arquivo indisponível", ex.Message, "Fechar"); }
    }
    private async void DeleteSaved(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedSavedQuery: not null } tab && await Dialogs.ChooseAsync(this, "Remover consulta salva", "Remover esta consulta do histórico local?", "Remover", "Cancelar") == "Remover")
            await tab.DeleteSavedQueryCommand.ExecuteAsync(null);
    }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } base.OnKeyDown(e); }
}
