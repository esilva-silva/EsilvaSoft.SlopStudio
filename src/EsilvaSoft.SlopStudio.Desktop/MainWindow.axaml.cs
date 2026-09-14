using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private bool _closing;
    private bool _restartAfterClose;
    public Task InitializationTask { get; private set; } = Task.CompletedTask;
    public Task ConnectionDialogTask { get; private set; } = Task.CompletedTask;
    public MainWindow()
    {
        InitializeComponent();
        Opened += InitializeWorkspace;
        AddHandler(KeyDownEvent, OnWorkspaceKeyDown, RoutingStrategies.Tunnel);
    }
    public WorkspaceViewModel? WorkspaceModel => DataContext as WorkspaceViewModel;
    private void InitializeWorkspace(object? sender, EventArgs e)
    {
        if (WorkspaceModel is not { } vm) return;
        vm.ThemeChanged += (_, _) => ApplyTheme();
        vm.LayoutChanged += (_, _) => WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(vm.ExplorerWidth);
        InitializationTask = vm.InitializeAsync();
        ApplyTheme();
    }
    private void ApplyTheme()
    {
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = WorkspaceModel?.Theme switch { "Claro" => ThemeVariant.Light, "Escuro" => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }
    private void OpenConnections(object? sender, RoutedEventArgs e) => ConnectionDialogTask = OpenConnectionDialogAsync();
    private async Task OpenConnectionDialogAsync()
    {
        if (WorkspaceModel is not { } vm) return;
        var dialog = new ConnectionsWindow(vm) { Width = Math.Min(800, Bounds.Width - 40), Height = Math.Min(560, Bounds.Height - 40) };
        await dialog.ShowDialog<bool>(this);
        ConnectionsButton.Focus();
        try { await vm.ReloadProfilesAsync(); } catch (Exception ex) { vm.SessionStatus = ex.Message; }
    }
    private void OpenExplorerCollection(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel?.SelectedNode is { } node) WorkspaceModel.OpenCollection(node);
    }
    private void ExplorerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenExplorerCollection(sender, e); e.Handled = true; }
        if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var host = Explorer.GetVisualDescendants().OfType<Grid>().FirstOrDefault(grid => ReferenceEquals(grid.DataContext, WorkspaceModel?.SelectedNode) && grid.ContextMenu is not null);
            if (host?.ContextMenu is { } menu) { menu.Open(host); e.Handled = true; }
        }
    }
    private void ExplorerResized(object? sender, VectorEventArgs e)
    {
        if (WorkspaceModel is { } vm) vm.ExplorerWidth = Math.Clamp(WorkspaceGrid.ColumnDefinitions[0].ActualWidth, 200, 420);
    }
    private async void OpenFile(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir script", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("JavaScript ou JSON") { Patterns = ["*.js", "*.json"] }, FilePickerFileTypes.All] });
        if (files.Count == 0) return;
        vm.NewTabCommand.Execute(null);
        try { await vm.ActiveTab!.OpenAsync(files[0].Path.LocalPath); }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, "Não foi possível abrir", ex.Message, "Fechar"); }
    }
    private async void SaveFile(object? sender, RoutedEventArgs e) { if (WorkspaceModel?.ActiveTab is { } tab) await SaveTabAsync(tab); }
    private async void SaveAs(object? sender, RoutedEventArgs e) { if (WorkspaceModel?.ActiveTab is { } tab) await SaveTabAsync(tab, choosePath: true); }
    public async Task<bool> SaveTabAsync(WorkspaceTabViewModel tab, bool choosePath = false)
    {
        var path = choosePath ? "" : tab.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Salvar script", SuggestedFileName = tab.IsConsole || tab.IsScript ? "consulta.js" : "consulta.json", FileTypeChoices = [new FilePickerFileType("Script / consulta") { Patterns = ["*.js", "*.json"] }] });
            if (file is null) return false;
            path = file.Path.LocalPath;
        }
        try { await tab.SaveAsync(path); return true; }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, "Arquivo não salvo", ex.Message, "Fechar"); return false; }
    }
    private async void CloseTab(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: WorkspaceTabViewModel tab }) await TryCloseTabAsync(tab);
    }
    private async Task<bool> StopTabAsync(WorkspaceTabViewModel tab)
    {
        if (!tab.IsRunning) return true;
        if (await Dialogs.ChooseAsync(this, "Execução em andamento", "Interromper esta operação e aguardar? Efeitos já enviados ao servidor não são revertidos.", "Interromper", "Cancelar") != "Interromper") return false;
        tab.CancelCommand.Execute(null);
        if (tab.ExecuteCommand.ExecutionTask is { } task) await task;
        return !tab.IsRunning;
    }
    private async Task<bool> TryCloseTabAsync(WorkspaceTabViewModel tab)
    {
        if (!await StopTabAsync(tab)) return false;
        if (tab.IsDirty)
        {
            var choice = await Dialogs.ChooseAsync(this, "Fechar aba", $"Salvar as alterações de {tab.Title}?", "Salvar", "Descartar", "Cancelar");
            if (choice is null or "Cancelar") return false;
            if (choice == "Salvar" && !await SaveTabAsync(tab)) return false;
        }
        WorkspaceModel!.RemoveTab(tab);
        try { await WorkspaceModel.SaveSessionAsync(); } catch (Exception ex) { await Dialogs.ChooseAsync(this, "Sessão não salva", ex.Message, "Fechar"); }
        return true;
    }
    private void OpenTools(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel?.ActiveTab is not { Profile: not null, IsConnected: true } tab) { WorkspaceModel!.SessionStatus = "Abra a conexão e escolha o destino da aba."; return; }
        var vm = new MainWindowViewModel(WorkspaceModel.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = tab.Profile, SelectedDatabase = tab.Database, SelectedCollection = tab.Collection,
            QueryFilter = tab.IsQuery ? tab.Text : "{}", QueryProjection = tab.Projection, QuerySort = tab.Sort,
            QueryHint = tab.Hint, QueryComment = tab.Comment, QueryCollation = tab.Collation,
            QueryLimit = tab.Limit, QuerySkip = tab.Skip, QueryBatchSize = tab.BatchSize, QueryMaxTimeMs = tab.MaxTimeMs,
            UuidRepresentation = WorkspaceModel.CaptureUuidPolicy().Resolve(tab.Profile.Id), IdentifierMode = WorkspaceModel.IdentifierMode
        };
        var window = new WorkspaceToolsWindow { DataContext = vm, Title = "Ferramentas · " + tab.Context };
        window.Closing += (_, args) => { if (vm.IsOperationRunning) { args.Cancel = true; vm.StatusMessage = "Cancele a operação e aguarde antes de fechar."; } };
        _ = window.ShowDialog(this);
    }
    private void OpenEnvironments(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } workspace) return;
        var model = new EnvironmentsViewModel(workspace.Workspace);
        model.Saved += (_, _) => workspace.InvalidateEnvironment();
        var window = new EnvironmentsWindow { DataContext = model, Width = Math.Min(760, Bounds.Width - 40), Height = Math.Min(540, Bounds.Height - 40) };
        _ = window.ShowDialog(this);
    }

    private void OpenPreferences(object? sender, RoutedEventArgs e) => ShowPreferences();

    /// <summary>Opens the owned preferences dialog and returns it for keyboard and rendering verification.</summary>
    public Window? ShowPreferences()
    {
        if (WorkspaceModel is not { } vm) return null;
        var window = new Window { Title = "Preferências", Width = 560, MinWidth = 460, MinHeight = 420, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(420, Math.Min(760, Bounds.Height - 40)), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "Aparência e rascunhos", Classes = { "section-title" } });
        stack.Children.Add(new TextBlock { Text = "Tamanho da fonte do editor (12–20)" });
        var size = new NumericUpDown { Minimum = 12, Maximum = 20, Value = (decimal)vm.CodeFontSize };
        size.ValueChanged += (_, _) => vm.CodeFontSize = (double)(size.Value ?? 14);
        stack.Children.Add(size);
        var recover = new CheckBox { Content = "Recuperar rascunhos ao reabrir", IsChecked = vm.RecoverDrafts };
        recover.IsCheckedChanged += (_, _) => vm.RecoverDrafts = recover.IsChecked == true;
        stack.Children.Add(recover);
        var profile = new CheckBox { Content = "Recuperar rascunhos desta conexão", IsChecked = vm.RecoverActiveProfile, IsEnabled = vm.CanSetProfileRecovery };
        profile.IsCheckedChanged += (_, _) => vm.RecoverActiveProfile = profile.IsChecked == true;
        stack.Children.Add(profile);
        vm.IdentifierPreferences.Load(vm.IdentifierMode);
        stack.Children.Add(new IdentifierModePanel { Name = "GlobalIdentifierPanel", DataContext = vm.IdentifierPreferences });
        vm.UuidPreferences.Load(vm.UuidRepresentation);
        vm.UuidPreferences.IsPreviewVisible = vm.IdentifierMode != Core.IdentifierRepresentationMode.ObjectId;
        stack.Children.Add(new UuidRepresentationPanel { Name = "GlobalUuidPanel", DataContext = vm.UuidPreferences });
        var autocomplete = new Button { Content = "Autocomplete…" };
        autocomplete.Click += (_, _) =>
        {
            vm.AutocompletePreferences.Load(vm.AutocompleteService.Settings);
            var preferences = new AutocompleteSettingsWindow { DataContext = vm.AutocompletePreferences, Height = Math.Min(680, Bounds.Height - 40) };
            _ = preferences.ShowDialog(window);
        };
        stack.Children.Add(autocomplete);
        stack.Children.Add(new TextBlock { Text = "O texto das abas é salvo localmente e pode conter dados sensíveis. Resultados e credenciais da conexão não são recuperados. A entrada JSON só é salva quando você habilita essa opção na aba.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } });
        var close = new Button { Content = "Concluir", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += async (_, _) =>
        {
            try { await vm.SaveSessionAsync(); window.Close(); }
            catch (Exception ex) { await Dialogs.ChooseAsync(window, "Preferências não salvas", ex.Message, "Fechar"); }
        };
        stack.Children.Add(close);
        window.Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; window.Close(); } };
        _ = window.ShowDialog(this);
        return window;
    }
    private async void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || WorkspaceModel is not { } vm) return;
        if (e.Key == Key.Escape && this.GetVisualDescendants().OfType<WorkspaceTabView>()
            .FirstOrDefault(v => v.DataContext == vm.ActiveTab)?.DismissCompletion() == true)
        { e.Handled = true; return; }
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.T) { e.Handled = true; vm.NewTabCommand.Execute(null); }
        else if (control && e.Key == Key.Tab && vm.Tabs.Count > 0)
        {
            e.Handled = true;
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            vm.ActiveTab = vm.Tabs[(vm.Tabs.IndexOf(vm.ActiveTab!) + step + vm.Tabs.Count) % vm.Tabs.Count];
        }
        else if (control && e.Key == Key.W && vm.ActiveTab is { } closingTab) { e.Handled = true; await TryCloseTabAsync(closingTab); }
        else if (e.Key == Key.F6)
        {
            e.Handled = true;
            var view = this.GetVisualDescendants().OfType<WorkspaceTabView>().FirstOrDefault(v => v.DataContext == vm.ActiveTab);
            var editor = view?.FindControl<SyntaxHighlighting.MongoTextEditor>("CodeEditor");
            if (editor?.IsKeyboardFocusWithin == true) Explorer.Focus(); else editor?.Focus();
        }
        else if (control && e.Key == Key.O) { e.Handled = true; OpenFile(sender, e); }
        else if (control && e.Key == Key.S) { e.Handled = true; if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) SaveAs(sender, e); else SaveFile(sender, e); }
        else if (e.Key == Key.F5 || (control && e.Key == Key.Enter))
        {
            e.Handled = true;
            if (vm.ActiveTab is { } tab)
            {
                var view = this.GetVisualDescendants().OfType<WorkspaceTabView>().FirstOrDefault(v => v.DataContext == tab);
                try { await tab.ExecuteCommand.ExecuteAsync(view?.ExecutionCode(control)); }
                catch (Exception ex) { tab.Errors = ex.Message; tab.ResultTabIndex = 2; }
            }
        }
        else if (e.Key == Key.Escape && vm.ActiveTab is { IsRunning: true } tab) { e.Handled = true; tab.CancelCommand.Execute(null); }
    }
    private const double CompactTopBarWidth = 1100;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Near the minimum width the top bar has no room for another labelled command.
        if (change.Property == ClientSizeProperty && UpdateButton is not null) UpdateButton.Classes.Set("compact", ClientSize.Width < CompactTopBarWidth);
    }

    /// <summary>Action started by the update button; exposed for UI verification.</summary>
    public Task UpdateActionTask { get; private set; } = Task.CompletedTask;
    private void UpdateButtonClick(object? sender, RoutedEventArgs e) => UpdateActionTask = HandleUpdateAsync();
    private async Task HandleUpdateAsync()
    {
        if (WorkspaceModel?.Updates is not { } updates) return;
        switch (updates.State)
        {
            case AppUpdateUiState.Available:
                await updates.DownloadCommand.ExecuteAsync(null);
                break;
            case AppUpdateUiState.ManualOnly when updates.Release is { } release:
                await Launcher.LaunchUriAsync(release.PageUrl);
                break;
            case AppUpdateUiState.Ready:
                var choice = await Dialogs.ChooseAsync(this, "Atualização pronta",
                    $"A versão {updates.ReadyVersion} será instalada quando o Slop Studio fechar. Reiniciar agora? Abas em execução e rascunhos seguem as confirmações de um fechamento normal.",
                    "Reiniciar agora", "Depois");
                if (choice == "Reiniciar agora") RestartForUpdate();
                break;
        }
    }

    /// <summary>Closes through the normal flow; the entry point installs the update and starts the new version.</summary>
    public void RestartForUpdate()
    {
        _restartAfterClose = true;
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        e.Cancel = true; base.OnClosing(e);
        if (_closing || WorkspaceModel is not { } vm) return;
        _closing = true;
        try
        {
            if (OwnedWindows.OfType<WorkspaceToolsWindow>().Any(w => w.DataContext is MainWindowViewModel { IsOperationRunning: true }))
            {
                await Dialogs.ChooseAsync(this, "Ferramenta em execução", "Conclua ou cancele a operação na janela de ferramentas antes de sair.", "Voltar");
                return;
            }
            foreach (var tab in vm.Tabs.ToArray())
            {
                if (!await StopTabAsync(tab)) return;
                if (tab.IsDirty && (!vm.RecoverDrafts || ((tab.Profile?.Id ?? tab.MissingProfileId) is Guid profileId && !vm.SnapshotAllowsRecovery(profileId))))
                    if (!await TryCloseTabAsync(tab)) return;
            }
            try { await vm.SaveSessionAsync(); }
            catch (Exception ex)
            {
                if (await Dialogs.ChooseAsync(this, "Sessão não salva", ex.Message + "\nFechar sem recuperar as alterações desta sessão?", "Fechar sem recuperar", "Cancelar") != "Fechar sem recuperar") return;
            }
            Program.RestartAfterExit = _restartAfterClose;
            _allowClose = true;
            vm.Dispose();
            Close();
        }
        finally
        {
            _closing = false;
            // A cancelled close must not turn a later ordinary exit into a restart.
            if (!_allowClose) _restartAfterClose = false;
        }
    }
}
