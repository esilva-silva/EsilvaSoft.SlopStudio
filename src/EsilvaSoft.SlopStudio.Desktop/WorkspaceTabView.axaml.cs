using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class WorkspaceTabView : UserControl
{
    public string? SelectedCode => CodeEditor.SelectedText;
    private CancellationTokenSource? _completionCancellation;
    public string? ExecutionCode(bool partial)
    {
        if (!partial) return null;
        if (!string.IsNullOrEmpty(SelectedCode)) return SelectedCode;
        if (DataContext is WorkspaceTabViewModel { IsConsole: true } tab)
        {
            var statement = tab.GetConsoleStatement(CodeEditor.CaretIndex);
            return statement.Length == 0 ? "// Nenhum statement no cursor." : tab.Text.Substring(statement.Start, statement.Length);
        }
        return null;
    }
    public WorkspaceTabView()
    {
        InitializeComponent();
        InitializeAutocomplete();
        InitializeResults();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WorkspaceTabViewModel tab) tab.ConfirmConsoleWrite = async (request, token) =>
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (TopLevel.GetTopLevel(this) is not Window owner) return false;
                    return await Dialogs.ChooseCancelableAsync(owner, "Confirmar escrita no Console", request.Context + "\n\nConfirmar o envio desta operação?", token, "Executar", "Cancelar") == "Executar";
                });
        };
        DetachedFromVisualTree += (_, _) => { _completionCancellation?.Cancel(); _formatCancellation?.Cancel(); };
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is MainWindow { WorkspaceModel: { } vm })
            {
                SplitGrid.RowDefinitions[0].Height = new GridLength(vm.EditorRatio, GridUnitType.Star);
                SplitGrid.RowDefinitions[2].Height = new GridLength(1 - vm.EditorRatio, GridUnitType.Star);
            }
        };
    }
    private void SplitResized(object? sender, VectorEventArgs e)
    {
        var top = SplitGrid.RowDefinitions[0].ActualHeight;
        var bottom = SplitGrid.RowDefinitions[2].ActualHeight;
        if (top + bottom > 0 && TopLevel.GetTopLevel(this) is MainWindow { WorkspaceModel: { } vm }) vm.EditorRatio = top / (top + bottom);
    }
    private CancellationTokenSource? _formatCancellation;
    private CancellationTokenSource? _validationCancellation;
    public Task ValidationTask { get; private set; } = Task.CompletedTask;
    private async void ValidateCode(object? sender, RoutedEventArgs e) { ValidationTask = ValidateCoreAsync(); await ValidationTask; }
    private async Task ValidateCoreAsync()
    {
        if (DataContext is not WorkspaceTabViewModel { IsRunning: false } tab) return;
        _validationCancellation?.Cancel();
        using var operation = tab.Operations.Begin("Validando sintaxe local", ApplicationOperationPriority.Normal);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _validationCancellation = cancellation;
        var document = CodeEditor.Document;
        var original = document.Text;
        var mode = tab.Mode;
        var profile = tab.Profile; var database = tab.Database; var collection = tab.Collection;
        var start = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectionStart : 0;
        var length = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectionLength : original.Length;
        try
        {
            var result = await tab.ValidateCodeAsync(original.Substring(start, length), mode == "Agregação", cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (DataContext != tab || CodeEditor.Document != document || document.Text != original || tab.IsRunning || tab.Mode != mode ||
                tab.Profile != profile || tab.Database != database || tab.Collection != collection)
            { operation.Complete(ApplicationOperationStatus.Warning, "Texto ou contexto alterado; diagnóstico descartado."); return; }
            var message = (length == original.Length ? "Editor: " : "Seleção: ") + result.Message;
            if (result.IsValid) { tab.Messages = message; tab.Errors = ""; tab.ResultTabIndex = 1; }
            else
            {
                tab.Errors = message; tab.ResultTabIndex = 2;
                CodeEditor.SelectionStart = start + Math.Clamp(result.Offset, 0, length);
                CodeEditor.SelectionEnd = start + Math.Clamp(result.Offset + result.Length, 0, length);
                CodeEditor.CaretIndex = CodeEditor.SelectionStart;
                CodeEditor.Focus();
            }
            operation.Complete(result.IsValid ? ApplicationOperationStatus.Success : ApplicationOperationStatus.Warning,
                result.IsValid ? "Sintaxe válida localmente" : "Diagnóstico disponível em Erros");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { operation.Complete(ApplicationOperationStatus.Cancelled, "Validação cancelada"); }
        catch (Exception ex)
        { tab.Errors = "Validação não concluída: " + OperationErrorMessages.Describe(ex); tab.ResultTabIndex = 2; operation.Complete(ApplicationOperationStatus.Error); }
        finally { if (ReferenceEquals(_validationCancellation, cancellation)) _validationCancellation = null; }
    }
    public Task FormattingTask { get; private set; } = Task.CompletedTask;
    private async void FormatCode(object? sender, RoutedEventArgs e) { FormattingTask = FormatCoreAsync(); await FormattingTask; }
    private async Task FormatCoreAsync()
    {
        if (DataContext is not WorkspaceTabViewModel tab) return;
        _formatCancellation?.Cancel();
        using var operation = tab.Operations.Begin("Formatando query/script", ApplicationOperationPriority.High);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _formatCancellation = cancellation;
        var document = CodeEditor.Document;
        var original = document.Text;
        var start = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectionStart : 0;
        var length = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectionLength : original.Length;
        try
        {
            var formatted = await tab.FormatCodeAsync(original.Substring(start, length), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (DataContext != tab || CodeEditor.Document != document || document.Text != original)
            { operation.Complete(ApplicationOperationStatus.Warning, "Texto alterado durante a formatação; resultado descartado."); return; }
            using (document.RunUpdate()) document.Replace(start, length, formatted);
            operation.Complete(ApplicationOperationStatus.Success, "Formatação concluída — Ctrl+Z para desfazer");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { operation.Complete(ApplicationOperationStatus.Cancelled, "Formatação cancelada"); }
        catch (Exception ex)
        { tab.Errors = "Não foi possível formatar: " + ex.Message; tab.ResultTabIndex = 2; operation.Complete(ApplicationOperationStatus.Error, "Formatação não concluída; consulte Erros."); }
        finally { if (ReferenceEquals(_formatCancellation, cancellation)) _formatCancellation = null; }
    }

    private async void ExportResults(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab || TopLevel.GetTopLevel(this) is not Window owner) return;
        var documents = tab.Documents.ToArray();
        try
        {
            var selection = await owner.StorageProvider.SaveFilePickerWithResultAsync(new FilePickerSaveOptions {
                Title = "Exportar página carregada — JSON ou CSV", SuggestedFileName = "resultados.json", DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("Extended JSON") { Patterns = ["*.json"] }, new FilePickerFileType("CSV · proteção de fórmulas") { Patterns = ["*.csv"] }] });
            if (selection.File is not { } file) return;
            // O nome digitado pelo usuário deve prevalecer sobre o filtro inicial
            // do diálogo quando a extensão indicar explicitamente CSV.
            var csv = string.Equals(Path.GetExtension(file.Path.LocalPath), ".csv", StringComparison.OrdinalIgnoreCase)
                || (selection.SelectedFileType is { } selectedType && selectedType.Patterns?.Contains("*.csv") == true);
            using var operation = tab.Operations.Begin($"Exportando página — {documents.Length} documentos", ApplicationOperationPriority.High);
            try
            {
                await tab.ExportResultPageAsync(file.Path.LocalPath, documents, csv,
                    (done, total) => operation.Report(done, total, $"Exportando página — {done:N0} de {total:N0} documentos"), operation.Token);
                tab.Messages = $"Página exportada — {documents.Length} documentos." + (csv ? " CSV: strings e cabeçalhos com prefixo de fórmula recebem apóstrofo; não há round-trip BSON garantido." : "");
                operation.Complete(ApplicationOperationStatus.Success, tab.Messages);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            { tab.Messages = "Exportação cancelada."; operation.Complete(ApplicationOperationStatus.Cancelled, tab.Messages); }
            catch
            { operation.Complete(ApplicationOperationStatus.Error, "Exportação não concluída; verifique destino e permissão de escrita."); throw; }
        }
        catch (Exception ex) { await Dialogs.ChooseAsync(owner, "Exportação não concluída", OperationErrorMessages.Describe(ex, export: true), "Fechar"); }
    }

    private async void ChooseTarget(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel { IsRunning: false } tab || TopLevel.GetTopLevel(this) is not MainWindow { WorkspaceModel: { } vm } owner) return;
        var window = new Window { Title = "Destino desta aba", Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "Conexão aberta" });
        var connections = new ComboBox { ItemsSource = vm.Roots.Where(r => r.IsConnected).ToArray(), SelectedItem = vm.Roots.FirstOrDefault(r => r.Profile.Id == tab.Profile?.Id),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ExplorerNodeViewModel>((node, _) => new TextBlock { Text = node?.Profile.Name }) };
        stack.Children.Add(connections);
        stack.Children.Add(new TextBlock { Text = "Banco" });
        var database = new ComboBox(); stack.Children.Add(database);
        stack.Children.Add(new TextBlock { Text = "Coleção (agregação legada)", IsVisible = !tab.IsConsole });
        var collection = new TextBox { Text = tab.Collection, IsVisible = !tab.IsConsole }; stack.Children.Add(collection);
        var info = new TextBlock { Text = "Abra uma conexão pelo botão Conexões antes de escolher o destino. A troca não executa o conteúdo.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } }; stack.Children.Add(info);
        void UpdateBanks()
        {
            var root = connections.SelectedItem as ExplorerNodeViewModel;
            database.ItemsSource = root?.Children.Select(c => c.Database).ToArray() ?? [];
            database.SelectedItem = root?.Children.FirstOrDefault(c => c.Database == tab.Database)?.Database ?? root?.Children.FirstOrDefault()?.Database;
        }
        connections.SelectionChanged += (_, _) => UpdateBanks(); UpdateBanks();
        var apply = new Button { Content = "Aplicar destino", HorizontalAlignment = HorizontalAlignment.Right, Classes = { "primary" } };
        apply.Click += (_, _) =>
        {
            var profile = (connections.SelectedItem as ExplorerNodeViewModel)?.Profile;
            if (profile is null || database.SelectedItem is not string bank) { info.Text = "Escolha uma conexão aberta e um banco."; return; }
            vm.BindActiveTab(profile, bank, collection.Text?.Trim() ?? "");
            window.Close();
        };
        stack.Children.Add(apply); window.Content = stack;
        window.KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Escape) { args.Handled = true; window.Close(); } };
        await window.ShowDialog(owner);
    }

    private async void ApplyAiProposal(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab || tab.AiProposal is not { } proposal || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (proposal.RequiresAdditionalConfirmation)
        {
            var warning = string.IsNullOrWhiteSpace(proposal.Warning)
                ? "A proposta contém uma operação de escrita ou destrutiva. A aplicação não executará o código."
                : proposal.Warning;
            if (await Dialogs.ChooseCancelableAsync(owner, "Confirmar aplicação de proposta", warning + "\n\nDeseja inserir esta proposta no editor?", CancellationToken.None, "Aplicar", "Cancelar") != "Aplicar")
                return;
        }
        if (!tab.CanApplyAiProposal(proposal))
        {
            tab.ChatStatusMessage = "A proposta ficou desatualizada porque o editor ou o destino mudou. Gere uma nova proposta.";
            tab.AiProposal = null;
            return;
        }

        var selectionStart = CodeEditor.SelectionStart;
        var selectionEnd = CodeEditor.SelectionEnd;
        var caret = CodeEditor.CaretIndex;
        _acceptingCompletion = true;
        try
        {
            // Replace through the editor's selected-text path so the normal undo stack remains available.
            CodeEditor.SelectionStart = 0;
            CodeEditor.SelectionEnd = (CodeEditor.Text ?? "").Length;
            CodeEditor.SelectedText = proposal.ProposedContent;
            var newLength = proposal.ProposedContent.Length;
            var restoredStart = Math.Clamp(selectionStart, 0, newLength);
            var restoredEnd = Math.Clamp(selectionEnd, restoredStart, newLength);
            CodeEditor.SelectionStart = restoredStart;
            CodeEditor.SelectionEnd = restoredEnd;
            CodeEditor.CaretIndex = Math.Clamp(caret, 0, newLength);
        }
        finally { _acceptingCompletion = false; }
        if (tab.CommitAiProposal(proposal)) CodeEditor.Focus();
    }

    private async void OpenHistory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab || TopLevel.GetTopLevel(this) is not Window owner) return;
        await tab.LoadHistoryCommand.ExecuteAsync(null);
        var window = new HistoryWindow { DataContext = tab };
        await window.ShowDialog(owner);
    }
    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && AcceptCompletion()) { e.Handled = true; return; }
        if (e.Key == Key.Escape && (CompletionPanel.IsVisible || _completionMenu is not null)) { InvalidateCompletion(); e.Handled = true; return; }
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; ShowSuggestions(sender, e); }
    }
    private async void ShowSuggestions(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab) return;
        InvalidateCompletion();
        if (!tab.Autocomplete.Settings.Enabled) return;
        var version = _completionSession.Version;
        var original = tab.Text;
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, original.Length);
        var prefix = original[..caret];
        var mode = tab.Mode;
        _completionCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _completionCancellation = cancellation;
        var profile = tab.Profile; var database = tab.Database; var collection = tab.Collection;
        var menu = new MenuFlyout();
        bool IsCurrent() => _completionSession.Version == version && CodeEditor.CaretIndex == caret && tab.Text == original
            && DataContext == tab && tab.Profile == profile && tab.Database == database && tab.Collection == collection && tab.Mode == mode && !cancellation.IsCancellationRequested;
        void AddInsertion(string text, string description, int start, int length, string? label = null)
        {
            var item = new MenuItem { Header = label ?? text };
            ToolTip.SetTip(item, description);
            item.Click += (_, _) =>
            {
                if (!IsCurrent()) return;
                CodeEditor.SelectionStart = start; CodeEditor.SelectionEnd = start + length;
                CodeEditor.SelectedText = text;
                CodeEditor.CaretIndex = start + text.Length; CodeEditor.Focus();
            };
            menu.Items.Add(item);
        }
        try
        {
            var request = new AutocompleteRequest(prefix[Math.Max(0, prefix.Length - AutocompleteRequest.MaximumContextCharacters)..],
                original[caret..Math.Min(original.Length, caret + AutocompleteRequest.MaximumContextCharacters)], tab.IsConsole ? "javascript" : mode);
            var completion = await tab.Autocomplete.GetCompletionAsync(request, cancellation.Token);
            if (!IsCurrent()) return;
            if (completion is not null) AddInsertion(completion.Text, completion.Description, caret, 0);
            if (tab.IsConsole)
            {
                var fields = tab.GetObservedCompletionFields(prefix);
                var suggestions = prefix.Contains(".aggregate(", StringComparison.Ordinal)
                    ? MqlAutocompleteService.GetAggregationSuggestions(prefix, fields) : MqlAutocompleteService.GetSuggestions(prefix, fields);
                foreach (var suggestion in suggestions)
                {
                    var insertion = MqlAutocompleteService.ApplySuggestion(prefix, suggestion);
                    AddInsertion(insertion, suggestion.Description, 0, caret, suggestion.Text);
                }
                try
                {
                    var completions = await tab.GetConsoleCompletionsAsync(prefix, cancellation.Token);
                    if (!IsCurrent()) return;
                    foreach (var item in completions) AddInsertion(item.Text, item.Description, item.Start, item.Length);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception) { tab.Messages = "Metadados de autocomplete indisponíveis; sugestões locais preservadas."; }
            }
            else
            {
                var fields = tab.GetObservedCompletionFields(prefix);
                var suggestions = tab.Mode == "Agregação" ? MqlAutocompleteService.GetAggregationSuggestions(prefix, fields) : MqlAutocompleteService.GetSuggestions(prefix, fields);
                foreach (var suggestion in suggestions)
                {
                    var insertion = MqlAutocompleteService.ApplySuggestion(prefix, suggestion);
                    AddInsertion(insertion, suggestion.Description, 0, caret, suggestion.Text);
                }
            }
            if (IsCurrent() && menu.Items.Count > 0)
            {
                _completionMenu = menu;
                menu.Closed += (_, _) => { if (ReferenceEquals(_completionMenu, menu)) _completionMenu = null; };
                menu.ShowAt(CodeEditor);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { tab.Messages = "Autocomplete indisponível nesta solicitação."; }
        finally { if (ReferenceEquals(_completionCancellation, cancellation)) _completionCancellation = null; }
    }
}
