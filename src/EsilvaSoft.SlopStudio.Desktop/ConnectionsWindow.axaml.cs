using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class ConnectionsWindow : Window
{
    private readonly WorkspaceViewModel? _workspace;
    public ConnectionsWindow() { InitializeComponent(); Opened += (_, _) => SearchBox.Focus(); }
    public ConnectionsWindow(WorkspaceViewModel workspace) : this()
    {
        _workspace = workspace;
        var model = new ConnectionsViewModel(workspace.Workspace, workspace);
        DataContext = model;
        ProfileUuidPanel.DataContext = model.Uuid;
        ProfileUuidPanel.IsVisible = true;
    }
    private async void OpenSelected(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || DataContext is not ConnectionsViewModel { SelectedChoice: not null, IsOpening: false } vm) return;
        vm.IsOpening = true; vm.Error = "";
        try { await _workspace.OpenConnectionAsync(vm.SelectedChoice.Profile); vm.IsOpening = false; Close(true); }
        catch (Exception ex) { vm.Error = ex.Message; }
        finally { vm.IsOpening = false; }
    }
    private void CloseDialog(object? sender, RoutedEventArgs e) { if (DataContext is not ConnectionsViewModel { IsOpening: true }) Close(false); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseDialog(this, e); }
        base.OnKeyDown(e);
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is ConnectionsViewModel { IsOpening: true }) e.Cancel = true;
        base.OnClosing(e);
    }
    private async void DeleteProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectionsViewModel { SelectedChoice: not null } vm) return;
        if (await Dialogs.ChooseAsync(this, "Remover perfil local", $"Remover o perfil {vm.SelectedChoice.Profile.Name}? Os bancos não serão alterados.", "Remover", "Cancelar") == "Remover")
            await vm.Editor.DeleteSelectedProfileCommand.ExecuteAsync(null);
    }
}
