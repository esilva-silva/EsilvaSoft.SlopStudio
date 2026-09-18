using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop;
using EsilvaSoft.SlopStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class TraditionalCompletionUiTests
{
    [Test]
    public async Task SnippetPlaceholdersFollowTabAndShiftTabAndUndoAsOneInsertion()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!; tab.TraditionalCompletion = new SnippetTraditionalProvider(); tab.Text = "";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo("first: second"));
            Assert.That(editor.SelectedText, Is.EqualTo("first"));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.SelectedText, Is.EqualTo("second"));
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Assert.That(editor.SelectedText, Is.EqualTo("first"));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.SelectionStart, Is.EqualTo(editor.SelectionEnd));
            editor.Document.UndoStack.Undo();
            Assert.That(tab.Text, Is.Empty);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task LateTraditionalResponseIsDiscardedAfterTheEditorChanges()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var delayed = new DelayedTraditionalProvider();
            var tab = workspace.ActiveTab!; tab.TraditionalCompletion = delayed; tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            await delayed.Entered.WaitAsync(TimeSpan.FromSeconds(3));
            editor.Text = "db.changed"; editor.CaretIndex = editor.Text.Length;
            delayed.Complete();
            await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.That(view.FindControl<Border>("TraditionalCompletionPanel")!.IsVisible, Is.False);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CtrlSpaceShowsAndAcceptsTraditionalCatalogItem()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var list = view.FindControl<ListBox>("TraditionalCompletionList")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            var documentation = view.FindControl<TextBlock>("TraditionalCompletionDocumentation")!;
            for (var i = 0; i < 100 && documentation.Text == "Carregando detalhes…"; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(documentation.Text, Does.Contain("Parâmetros:").Or.Contain("Categoria:").Or.Contain("Retorna:"),
                "A documentação do item inicialmente destacado é resolvida sem abrir outra lista.");
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.Width = 960; window.Height = 620; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"traditional-list-{theme}-960.png"), new PngBitmapEncoderOptions());
            }
            list.SelectedItem = ((IEnumerable<CompletionItem>)list.ItemsSource!).First(item => item.Label == "getCollection");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo("db.getCollection(\"colecao\")"));
            Assert.That(editor.SelectedText, Is.EqualTo("colecao"));
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.Width = 960; window.Height = 620; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"traditional-completion-{theme}-960.png"), new PngBitmapEncoderOptions());
            }
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CtrlPeriodOpensTheTraditionalCompletionList()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.OemPeriod, RawInputModifiers.Control, PhysicalKey.Period, null);
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(panel.IsVisible, Is.False);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task PersistedCompletionShortcutReplacesTheDefaultInTheEditor()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.KeyBindings = EditorKeyBindings.Resolve(new EditorKeyBindings
            {
                Bindings = new() { [EditorCommandIds.CompletionShow] = ["Ctrl+Shift+Space"] }
            });
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.OemPeriod, RawInputModifiers.Control, PhysicalKey.Period, null);
            await Task.Delay(50); Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.False);
            window.KeyPress(Key.Space, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && !panel.IsVisible; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TypingATriggerCharacterOpensTheListOnlyWhenAutoOpenIsEnabled()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;

            // Default (CompletionAutoOpenOnTrigger = false): typing "." never opens the list by itself.
            editor.Text = "db."; editor.CaretIndex = editor.Text.Length;
            await Task.Delay(30); Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.False, "CompletionAutoOpenOnTrigger é falso por padrão.");

            await tab.Autocomplete.ConfigureAsync(new AutocompleteSettings { CompletionAutoOpenOnTrigger = true });
            editor.Text = "db"; editor.CaretIndex = editor.Text.Length; Dispatcher.UIThread.RunJobs();
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Text = "db."; editor.CaretIndex = editor.Text.Length;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True, "Com a preferência ativa, o caractere de disparo abre a lista sem Ctrl+Espaço.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task StatusTextIsDistinctForUnavailableErrorAndNoSuggestionsStates()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;

            tab.TraditionalCompletion = null;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.True);
            var unavailable = status.Text;
            Assert.That(unavailable, Is.EqualTo("Sugestões tradicionais indisponíveis nesta aba."));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            tab.TraditionalCompletion = new ThrowingTraditionalProvider();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && status.Text == "Carregando sugestões…"; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            var error = status.Text;
            Assert.That(error, Is.EqualTo("Não foi possível carregar sugestões agora."));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            tab.TraditionalCompletion = new EmptyTraditionalProvider();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && status.Text == "Carregando sugestões…"; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            var noMatches = status.Text;
            Assert.That(noMatches, Is.EqualTo("Nenhuma sugestão corresponde ao texto atual"));

            Assert.That(new[] { unavailable, error, noMatches }.Distinct().Count(), Is.EqualTo(3),
                "Indisponível, erro e sem sugestões são estados distintos em pt-BR.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task LosingEditorFocusClosesTheTraditionalCompletionList()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var other = view.FindControl<MongoTextEditor>("ResultJson")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);

            other.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.False, "Perder o foco do editor fecha a lista de sugestões tradicionais.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    private sealed class ThrowingTraditionalProvider : ICompletionProvider
    {
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Falha simulada de fornecedor, usada para verificar o estado de erro.");
    }

    private sealed class EmptyTraditionalProvider : ICompletionProvider
    {
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CompletionResponse(request, new(request.Context.Version, [], false)));
    }

    private sealed class DelayedTraditionalProvider : ICompletionProvider
    {
        private readonly TaskCompletionSource<CompletionRequest> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public Task<CompletionRequest> Entered => _entered.Task;
        public void Complete() => _release.TrySetResult();

        public async ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult(request);
            await _release.Task;
            var item = new CompletionItem("test", "changed", "teste", CompletionItemKind.Text,
                new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, "changed"), "changed", 0, CompletionSource.Catalog);
            return new CompletionResponse(request, new(request.Context.Version, [item], false));
        }
    }

    private sealed class SnippetTraditionalProvider : ICompletionProvider
    {
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        {
            var item = new CompletionItem("snippet", "condição", "Snippet de teste", CompletionItemKind.Snippet,
                new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, "${1:first}: ${2:second}$0", true), "condição", 0, CompletionSource.Snippet);
            return ValueTask.FromResult(new CompletionResponse(request, new(request.Context.Version, [item], false)));
        }
    }
}
