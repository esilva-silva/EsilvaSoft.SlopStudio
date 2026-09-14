using EsilvaSoft.SlopStudio.Desktop.SyntaxHighlighting;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

public class MongoTestProxy : DispatchProxy
{
    public Func<string, object?[], object?>? Handler { get; set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler!(targetMethod!.Name, args ?? []);
}

internal sealed class WorkspaceTestContext : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "slop-ui-" + Guid.NewGuid().ToString("N"));
    public LiteDbConnectionProfileRepository Repository { get; }
    public WorkspaceService Workspace { get; }
    public ControlledScripts Scripts { get; } = new();
    public MongoTestProxy Mongo { get; }
    public WorkspaceTestContext(IConsoleHistoryRepository? historyOverride = null)
    {
        Repository = new LiteDbConnectionProfileRepository(Path.Combine(_directory, "workspace.db"));
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        Mongo = (MongoTestProxy)mongo;
        Mongo.Handler = (name, _) => name switch
        {
            "GetDatabaseNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["loja", "auditoria"]),
            "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["clientes", "pedidos"]),
            _ => throw new NotSupportedException(name)
        };
        var secrets = new SessionConnectionSecretStore();
        var console = new ConsoleRuntime(Repository, Repository, secrets, new WorkspaceConsoleSession(mongo), Repository, Repository);
        Workspace = new WorkspaceService(Repository, Repository, Repository, Repository, Repository, mongo, Scripts, new LocalScriptFileService(), secrets, Repository, new ExplorerMetadataService(mongo), console, historyOverride ?? Repository, formatter: new MongoCodeFormatter(), validator: new MongoCodeValidator());
    }
    public void Dispose() { Repository.Dispose(); Directory.Delete(_directory, true); }
}

internal sealed class WorkspaceConsoleSession(IMongoWorkspaceService mongo) : IConsoleDatabaseSessionFactory, IConsoleDatabaseSession
{
    private IReadOnlyList<ConnectionProfile> _profiles = [];
    public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) => new WorkspaceConsoleSession(mongo) { _profiles = resolvedProfiles };
    public async Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken)
    {
        using var args = System.Text.Json.JsonDocument.Parse(operation.ArgumentsJson);
        var root = args.RootElement;
        if (operation.Method != "find") throw new NotSupportedException(operation.Method);
        var query = new MongoQuery(operation.Database, operation.Collection, root[0].GetRawText(), Limit: root[2].GetProperty("limit").GetInt32());
        var page = await mongo.QueryAsync(_profiles.Single(p => p.Id == operation.ProfileId), query, cancellationToken);
        return "{\"value\":[" + string.Join(",", page.Documents) + "],\"truncated\":" + (page.IsTruncated ? "true" : "false") + "}";
    }
    public void Dispose() { }
}

internal sealed class ControlledScripts : IScriptExecutionService
{
    public List<(Guid Profile, string? Database, string Script, TaskCompletionSource<ScriptExecutionResult> Completion)> Calls { get; } = [];
    public Task<ScriptExecutionResult> ExecuteAsync(ConnectionProfile profile, string script, string? inputJson = null, string? database = null, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        var source = new TaskCompletionSource<ScriptExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Calls.Add((profile.Id, database, script, source));
        return source.Task.WaitAsync(cancellationToken);
    }
}

internal sealed class FailingSessionRepository : IWorkspaceSessionRepository
{
    public bool FailLoad { get; set; }
    public bool FailSave { get; set; } = true;
    public int SaveAttempts { get; private set; }
    public Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default) =>
        FailLoad ? Task.FromException<WorkspaceSession>(new InvalidDataException("snapshot ilegível")) : Task.FromResult(new WorkspaceSession());
    public Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default)
    {
        SaveAttempts++;
        return FailSave ? Task.FromException(new IOException("disco indisponível")) : Task.CompletedTask;
    }
}

[TestFixture]
public sealed class WorkspaceBehaviorTests
{
    [Test]
    public async Task DebouncePersistsLatestDraftAndOnlyOptedInInput()
    {
        using var context = new WorkspaceTestContext();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync();
        vm.ActiveTab!.Text = "first"; vm.ActiveTab.Text = "latest";
        vm.ActiveTab.InputJson = "{\"parameter\":42}"; vm.ActiveTab.PersistInput = true;
        await Task.Delay(1100);
        var session = await context.Repository.LoadSessionAsync();
        Assert.That(session.Tabs.Single().Text, Is.EqualTo("latest"));
        Assert.That(session.Tabs.Single().InputJson, Is.EqualTo("{\"parameter\":42}"));
    }
    [Test]
    public async Task FailedSaveIsVisibleKeepsDraftAndCanBeRetried()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FailingSessionRepository();
        using var vm = new WorkspaceViewModel(context.Workspace, repository);
        await vm.InitializeAsync(); vm.ActiveTab!.Text = "keep this draft";
        Assert.ThrowsAsync<IOException>(() => vm.SaveSessionAsync());
        Assert.That(vm.SessionStatus, Does.Contain("não salvo"));
        Assert.That(vm.ActiveTab.Text, Is.EqualTo("keep this draft"));
        repository.FailSave = false; await vm.SaveSessionAsync();
        Assert.That(vm.SessionStatus, Does.Contain("atualizados"));
    }

    [Test]
    public async Task UnreadableSessionIsNeverOverwrittenByAutomaticDefaults()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FailingSessionRepository { FailLoad = true };
        using var vm = new WorkspaceViewModel(context.Workspace, repository);
        await vm.InitializeAsync(); vm.ActiveTab!.Text = "new content";
        Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveSessionAsync());
        Assert.That(repository.SaveAttempts, Is.Zero);
        Assert.That(vm.SessionStatus, Does.Contain("recuperar"));
    }

    [Test]
    public async Task EditingProfileDisconnectsOldExplorerAndUpdatesReadOnlyPolicy()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        await context.Repository.SaveAsync(profile);
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync(); await vm.OpenConnectionAsync(profile); vm.BindActiveTab(profile, "loja", "clientes");
        await context.Repository.SaveAsync(profile with { ConnectionString = "mongodb://other-host", IsReadOnly = true });
        await vm.ReloadProfilesAsync();
        Assert.That(vm.Roots, Has.Count.EqualTo(1));
            Assert.That(vm.Roots[0].IsConnected, Is.False);
            Assert.That(vm.Roots[0].Children.All(n => n.Kind == ExplorerNodeKind.Placeholder), Is.True);
        Assert.That(vm.ActiveTab!.IsConnected, Is.False);
        Assert.That(vm.ActiveTab.Profile!.IsReadOnly, Is.True);
        Assert.That(vm.ActiveTab.Database, Is.EqualTo("loja"));
    }

    [Test]
    public async Task OldWorkspaceProfilesRemainReadableAfterSessionMigration()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Original", "mongodb://localhost");
        await context.Repository.SaveAsync(profile);
        Assert.That((await context.Repository.LoadSessionAsync()).Tabs, Is.Empty);
        await context.Repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { Theme = "Escuro", CodeFontSize = 18 } });
        Assert.That((await context.Repository.GetAllAsync()).Single(), Is.EqualTo(profile));
        var preferences = (await context.Repository.LoadSessionAsync()).Preferences;
        Assert.That(preferences.Theme, Is.EqualTo("Escuro"));
        Assert.That(preferences.CodeFontSize, Is.EqualTo(18));
    }

    [Test]
    public async Task FailedSelectionNeverFallsBackToExecutingWholeScript()
    {
        using var context = new WorkspaceTestContext();
        var tab = MakeTab(context, "A");
        var run = tab.ExecuteCommand.ExecuteAsync("invalid partial }");
        context.Scripts.Calls[0].Completion.SetException(new InvalidOperationException("syntax error"));
        await run;
        Assert.That(context.Scripts.Calls, Has.Count.EqualTo(1));
        Assert.That(context.Scripts.Calls[0].Script, Is.EqualTo("invalid partial }"));
        Assert.That(tab.Errors, Is.EqualTo("Operação não concluída: syntax error"));
    }

    [Test]
    public async Task TabsKeepDestinationAndResultsWhenCompletionsArriveOutOfOrder()
    {
        using var context = new WorkspaceTestContext();
        var a = MakeTab(context, "A"); var b = MakeTab(context, "B");
        var first = a.ExecuteCommand.ExecuteAsync(null); var second = b.ExecuteCommand.ExecuteAsync("slop.results.emit({ b: 2 });");
        a.Database = "edited-after-start";
        context.Scripts.Calls[1].Completion.SetResult(new(0, ["{\"b\":2}"], "B", "", TimeSpan.FromMilliseconds(5)));
        await second;
        Assert.Multiple(() =>
        {
            Assert.That(a.IsRunning, Is.True);
            Assert.That(b.Results, Does.Contain("\"b\": 2"));
            Assert.That(context.Scripts.Calls[0].Database, Is.EqualTo("A"));
            Assert.That(context.Scripts.Calls[1].Script, Is.EqualTo("slop.results.emit({ b: 2 });"));
        });
        context.Scripts.Calls[0].Completion.SetResult(new(0, ["{\"a\":1}"], "A", "", TimeSpan.FromMilliseconds(20)));
        await first;
        Assert.That(a.Results, Does.Contain("\"a\": 1"));
        Assert.That(b.Results, Does.Not.Contain("\"a\": 1"));
    }

    [Test]
    public async Task CancelOnlyAffectsItsOwnTabAndReportsUncertainEffects()
    {
        using var context = new WorkspaceTestContext();
        var a = MakeTab(context, "A"); var b = MakeTab(context, "B");
        var first = a.ExecuteCommand.ExecuteAsync(null); var second = b.ExecuteCommand.ExecuteAsync(null);
        a.CancelCommand.Execute(null); await first;
        Assert.That(a.Messages, Does.Contain("incerto"));
        Assert.That(b.IsRunning, Is.True);
        b.CancelCommand.Execute(null); await second;
    }

    [Test]
    public void ReadOnlyAndDisconnectedScriptsCannotExecute()
    {
        using var context = new WorkspaceTestContext();
        var tab = MakeTab(context, "A"); tab.Profile = tab.Profile! with { IsReadOnly = true };
        Assert.That(tab.ExecuteCommand.CanExecute(null), Is.False);
        tab.Profile = tab.Profile with { IsReadOnly = false }; tab.IsConnected = false;
        Assert.That(tab.ExecuteCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task SessionRestoresOrderTextAndDestinationWithoutConnectingOrResults()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        await context.Repository.SaveAsync(profile);
        using (var vm = new WorkspaceViewModel(context.Workspace, context.Repository))
        {
            await vm.InitializeAsync(); await vm.OpenConnectionAsync(profile);
            vm.BindActiveTab(profile, "loja", "clientes");
            vm.ActiveTab!.Text = "const rascunho = 1;"; vm.ActiveTab.InputJson = "{\"sensitive\":1}";
            vm.NewTabCommand.Execute(null); vm.ActiveTab!.Text = "const segundo = 2;";
            await vm.SaveSessionAsync();
        }
        using var restored = new WorkspaceViewModel(context.Workspace, context.Repository);
        await restored.InitializeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(restored.Tabs, Has.Count.EqualTo(2));
            Assert.That(restored.Tabs[0].Text, Is.EqualTo("const rascunho = 1;"));
            Assert.That(restored.Tabs[0].Database, Is.EqualTo("loja"));
            Assert.That(restored.Tabs[0].InputJson, Is.EqualTo("{}"));
            Assert.That(restored.ActiveTab, Is.SameAs(restored.Tabs[1]));
            Assert.That(restored.Roots, Has.Count.EqualTo(1));
            Assert.That(restored.Roots[0].IsConnected, Is.False);
            Assert.That(restored.Roots[0].Children.All(n => n.Kind == ExplorerNodeKind.Placeholder), Is.True);
            Assert.That(restored.Tabs.All(t => !t.IsConnected && t.Documents.Count == 0), Is.True);
        });
    }

    [Test]
    public async Task PersistencePolicyFiltersExcludedProfilesAndCanRemoveAllDrafts()
    {
        using var context = new WorkspaceTestContext();
        var excluded = Guid.NewGuid(); var retained = Guid.NewGuid();
        var session = new WorkspaceSession { Preferences = new() { ExcludedProfileIds = [excluded] }, Tabs = [new() { ProfileId = excluded, Text = "omit" }, new() { ProfileId = retained, Text = "keep" }] };
        await context.Repository.SaveSessionAsync(session);
        Assert.That((await context.Repository.LoadSessionAsync()).Tabs.Select(t => t.Text), Is.EqualTo(Enumerable.Repeat("keep", 1)));
        await context.Repository.SaveSessionAsync(session with { Preferences = new() { RecoverDrafts = false } });
        Assert.That((await context.Repository.LoadSessionAsync()).Tabs, Is.Empty);
    }

    [Test]
    public async Task RemovingTabRemovesItsPersistedDraft()
    {
        using var context = new WorkspaceTestContext();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync(); vm.ActiveTab!.Text = "discard me"; await vm.SaveSessionAsync();
        vm.RemoveTab(vm.ActiveTab); await vm.SaveSessionAsync();
        Assert.That((await context.Repository.LoadSessionAsync()).Tabs, Is.Empty);
    }

    [Test]
    public async Task ExplorerLoadsOnlyExpandedBankAndNeverExecutesQuery()
    {
        using var context = new WorkspaceTestContext();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync(); await vm.OpenConnectionAsync(ConnectionProfile.Create("Local", "mongodb://localhost"));
        var bank = vm.Roots[0].Children[0]; await bank.LoadAsync();
        vm.OpenCollection(bank.Children[0]);
        Assert.That(vm.ActiveTab!.Mode, Is.EqualTo("Console"));
        Assert.That(vm.ActiveTab.Text, Does.Contain("db.getCollection("));
        Assert.That(vm.ActiveTab.Text, Is.EqualTo("db.getCollection(\"clientes\").find({}).limit(100)"));
        Assert.That(context.Scripts.Calls, Is.Empty);
        Assert.That(vm.Roots[0].Children[1].Children[0].Name, Is.EqualTo("Expandir para carregar"));
    }

    [Test]
    public void MongoshSelectsDatabaseUsingJsonLiteralWithoutChangingAuthenticationUri()
    {
        const string database = "tenant\"; throw 123; //";
        var script = MongoshScriptExecutionService.BuildScript("{}", "print(db.getName());", database);
        var selection = "db = db.getSiblingDB(" + System.Text.Json.JsonSerializer.Serialize(database) + ");";
        Assert.That(script, Does.Contain(selection));
        Assert.That(script.IndexOf("db = connect(__slopUri)", StringComparison.Ordinal), Is.LessThan(script.IndexOf(selection, StringComparison.Ordinal)));
        Assert.That(script.IndexOf(selection, StringComparison.Ordinal), Is.LessThan(script.IndexOf("print(db.getName());", StringComparison.Ordinal)));
        Assert.That(script, Does.Contain("print(db.getName());"));
    }

    private static WorkspaceTabViewModel MakeTab(WorkspaceTestContext context, string name) => new(context.Workspace) { Mode = "Script", Profile = ConnectionProfile.Create(name, "mongodb://localhost"), Database = name, Text = "slop.results.emit({ ok: 1 });", IsConnected = true };
}

public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont();
}

[TestFixture, NonParallelizable]
public sealed class WorkspaceRenderingTests
{
    private static readonly double[] Scales = [1, 1.5, 2];
    [Test]
    public async Task RenderThemesSizesAndScalingAndCheckEditorResultGeometry()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("Desenvolvimento · Loja", "mongodb://localhost:27017");
            await context.Repository.SaveAsync(profile);
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = vm };
            window.Show();
            await window.InitializationTask;
            await vm.OpenConnectionAsync(profile);
            var bank = vm.Roots[0].Children[0]; await bank.LoadAsync(); bank.IsExpanded = true;
            vm.OpenCollection(bank.Children[0]);
            vm.ActiveTab!.Text = "{\n  \"ativo\": true,\n  \"endereco.cidade\": \"São Paulo\"\n}";
            vm.ActiveTab.Results = "{\n  \"_id\": { \"$oid\": \"64b000000000000000000001\" },\n  \"nome\": \"Ana Silva\",\n  \"ativo\": true,\n  \"endereco\": { \"cidade\": \"São Paulo\" }\n}\n\n{\n  \"nome\": \"Bruno Costa\",\n  \"ativo\": true\n}";
            vm.ActiveTab.Metrics = "2 documentos · limite 100 · 12 ms";
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in Scales)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        AssertContrast(theme, "PrimaryText", "PanelBackground", 4.5);
                        AssertContrast(theme, "SecondaryText", "PanelBackground", 4.5);
                        AssertContrast(theme, "PrimaryText", "SelectionBrush", 4.5);
                        AssertContrast(theme, "OnAccentBrush", "AccentBrush", 4.5);
                        AssertContrast(theme, "OnAccentBrush", "AccentHoverBrush", 4.5);
                        AssertContrast(theme, "OnAccentBrush", "AccentPressedBrush", 4.5);
                        AssertContrast(theme, "ControlBorderBrush", "PanelBackground", 3);
                        AssertContrast(theme, "BrandAccentBrush", "PanelBackground", 4.5);
                        AssertContrast(theme, "SecondaryText", "SecondaryBackground", 4.5);
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        var toolbar = (Grid)window.FindControl<Button>("ConnectionsButton")!.Parent!;
                        foreach (var button in toolbar.Children.OfType<Button>())
                        {
                            var position = button.TranslatePoint(new Point(), window)!.Value;
                            Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
                            Assert.That(position.X + button.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
                        }
                        using var frame = window.CaptureRenderedFrame();
                        Assert.That(frame, Is.Not.Null);
                        frame!.Save(Path.Combine(directory, $"workspace-{theme}-{size.Width}x{size.Height}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().First();
                        var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
                        Assert.That(editor.Bounds.Height, Is.GreaterThan(50));
                        Assert.That(editor.Bounds.Width, Is.GreaterThan(400));
                        Assert.That(view.Bounds.Bottom, Is.LessThanOrEqualTo(window.Bounds.Height));
                    }
            window.SetRenderScaling(1);
            var connections = new ConnectionsWindow(vm);
            var modal = connections.ShowDialog<bool>(window);
            connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(connections.FocusManager!.GetFocusedElement(), Is.SameAs(connections.FindControl<TextBox>("SearchBox")));
            Assert.That(connections.FindControl<TextBox>("SearchBox")!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Placeholder").Opacity, Is.EqualTo(1));
            using (var frame = connections.CaptureRenderedFrame()) frame!.Save(Path.Combine(directory, "connections-dark.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            var connectionsVm = (ConnectionsViewModel)connections.DataContext!;
            connectionsVm.Editor.ShowProfileEditorCommand.Execute(null);
            connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var saveProfile = connections.FindControl<Button>("SaveProfileButton")!;
            var savePosition = saveProfile.TranslatePoint(new Point(), connections)!.Value;
            Assert.That(savePosition.Y + saveProfile.Bounds.Height, Is.LessThanOrEqualTo(connections.ClientSize.Height));
            Assert.That(saveProfile.IsVisible, Is.True);
            using (var frame = connections.CaptureRenderedFrame()) frame!.Save(Path.Combine(directory, "connection-form-dark.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using (var frame = connections.CaptureRenderedFrame()) frame!.Save(Path.Combine(directory, "connection-form-light.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            connections.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await modal;
            Assert.That(vm.ActiveTab.IsRunning, Is.False);
            var environmentModel = new EnvironmentsViewModel(context.Workspace) { EnvironmentName = "Cliente customizado de homologação" };
            environmentModel.AddEnvironmentCommand.Execute(null);
            environmentModel.Key = "MONGO_PASSWORD"; environmentModel.Value = "valor-sintético";
            environmentModel.SetValueCommand.Execute(null);
            var environments = new EnvironmentsWindow { DataContext = environmentModel };
            var environmentModal = environments.ShowDialog(window);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(600, 420), new Size(760, 540), new Size(900, 650) })
                    foreach (var scale in Scales)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        environments.Width = size.Width; environments.Height = size.Height; environments.SetRenderScaling(scale);
                        environments.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        var keysPanel = environments.FindControl<StackPanel>("KeysPanel")!;
                        var valuePanel = environments.FindControl<StackPanel>("ValuePanel")!;
                        Assert.That(keysPanel.Bounds.Right, Is.LessThanOrEqualTo(valuePanel.Bounds.Left));
                        using var frame = environments.CaptureRenderedFrame();
                        Assert.That(frame, Is.Not.Null);
                        frame!.Save(Path.Combine(directory, $"environments-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            environments.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await environmentModal;
            // Exercise the real shell event route: Escape dismisses a connection dialog without canceling a running tab.
            vm.ActiveTab.Mode = "Script";
            var run = vm.ActiveTab.ExecuteCommand.ExecuteAsync("print('running');");
            window.FindControl<Button>("ConnectionsButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var owned = window.OwnedWindows.OfType<ConnectionsWindow>().Single();
            owned.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await window.ConnectionDialogTask;
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.ActiveTab.IsRunning, Is.True);
            Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(window.FindControl<Button>("ConnectionsButton")));
            vm.ActiveTab.CancelCommand.Execute(null); await run;
            var active = vm.ActiveTab;
            window.KeyPress(Key.Tab, RawInputModifiers.Control, PhysicalKey.Tab, null);
            Assert.That(vm.ActiveTab, Is.Not.SameAs(active));
            window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
            Assert.That(window.FocusManager.GetFocusedElement(), Is.TypeOf<AvaloniaEdit.Editing.TextArea>());
            vm.ActiveTab!.CodeFontSize = 20;
            window.Width = 960; window.Height = 620; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(directory, "workspace-font20.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            foreach (var tab in vm.Tabs.ToArray()) vm.RemoveTab(tab);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"workspace-empty-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                Assert.That(window.Icon, Is.Not.Null);
                Assert.That(window.GetVisualDescendants().OfType<Image>().Count(i => i.IsEffectivelyVisible && i.Source is not null), Is.EqualTo(2));
            }
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static void AssertContrast(ThemeVariant theme, string foreground, string background, double minimum)
    {
        Avalonia.Application.Current!.Resources.TryGetResource(foreground, theme, out var foregroundResource);
        Avalonia.Application.Current.Resources.TryGetResource(background, theme, out var backgroundResource);
        var a = Luminance(((Avalonia.Media.ISolidColorBrush)foregroundResource!).Color);
        var b = Luminance(((Avalonia.Media.ISolidColorBrush)backgroundResource!).Color);
        Assert.That((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05), Is.GreaterThanOrEqualTo(minimum), $"{theme}: {foreground}/{background}");
    }
    private static double Luminance(Avalonia.Media.Color color)
    {
        static double Linear(byte component) { var value = component / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
