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
using EsilvaSoft.SlopStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.LocalAi.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class WorkspaceTabAiChatLocalizationUiTests
{
    [TestCase("pt-BR", "Proposta pronta para revisão. Nada foi alterado no editor.")]
    [TestCase("en", "Proposal ready for review. Nothing changed in the editor.")]
    [TestCase("es", "Propuesta lista para revisión. No se cambió nada en el editor.")]
    [TestCase("zh-CN", "提案已准备好供审阅。编辑器未发生更改。")]
    public async Task AssistantPanelActionsAndStatesAreLocalizedAndRenderInBothThemes(string language, string expectedReady)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var previousLanguage = LocalizationViewModel.Current.Language;
            var previousTheme = Avalonia.Application.Current!.RequestedThemeVariant;
            LocalizationViewModel.Current.Language = language;
            using var context = new WorkspaceTestContext();
            var autocomplete = new AutocompleteService();
            await autocomplete.ConfigureAsync(new AutocompleteSettings { LocalAiContextEnabled = false });
            var chat = new ScriptedChatService();
            using var tab = new WorkspaceTabViewModel(context.Workspace)
            {
                Autocomplete = autocomplete,
                AiChat = chat,
                Text = "db.customers.find({})",
                Database = "shop",
                Collection = "customers"
            };
            var view = new WorkspaceTabView { DataContext = tab };
            var window = new Window { Width = 1120, Height = 760, Content = view };
            var evidenceDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence", "ai-chat-localization");
            Directory.CreateDirectory(evidenceDirectory);

            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                // Global privacy opt-out presents a visible no-context explanation and dispatches no request.
                tab.ChatInput = "Explique esta consulta";
                await tab.SendAiChatCommand.ExecuteAsync(null);
                Assert.That(tab.ChatStatus, Is.EqualTo(AiChatStatus.NoContext));
                AssertLocalizedPanel(view, LocalizationViewModel.Current.Resolve("aiContextDisabled"));
                Assert.That(chat.RequestCount, Is.Zero);

                // A user cancellation reaches the in-flight request and leaves a localized canceled state.
                await autocomplete.ConfigureAsync(autocomplete.Settings with { LocalAiContextEnabled = true });
                chat.BlockNextRequest();
                tab.ChatInput = "Explique esta consulta";
                await tab.SendAiChatCommand.ExecuteAsync(null);
                Assert.That(tab.HasAiContextPreview, Is.True);
                Assert.That(tab.AiContextPreview, Does.Contain("db.customers.find({})").And.Contain("Explique esta consulta"));
                Assert.That(tab.AiContextPreview, Does.Contain(LocalizationViewModel.Current.Resolve("aiPreviewInstruction")));
                Assert.That(chat.RequestCount, Is.Zero, "A prévia não chama o modelo.");
                var pending = tab.SendAiChatCommand.ExecuteAsync(null);
                await chat.RequestEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
                Assert.That(tab.ChatStatus, Is.EqualTo(AiChatStatus.Loading));
                tab.CancelAiChatCommand.Execute(null);
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(tab.ChatStatus, Is.EqualTo(AiChatStatus.Canceled));
                AssertLocalizedPanel(view, LocalizationViewModel.Current.Resolve("aiAnalysisCancelled"));

                // A successful response is a reviewable proposal; the editor remains unchanged until Apply.
                chat.Responder = (_, _) => Task.FromResult<AiChatResponse?>(new AiChatResponse(
                    "Filtra clientes ativos.", "db.customers.find({ ativo: true })", "+ ativo: true", false, ""));
                tab.ChatInput = "Filtre clientes ativos";
                await tab.SendAiChatCommand.ExecuteAsync(null);
                Assert.That(tab.AiContextPreview, Does.Contain("db.customers.find({})"));
                AssertLocalizedPanel(view, LocalizationViewModel.Current.Resolve("aiContextPreviewReview"));
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Avalonia.Application.Current.RequestedThemeVariant = theme;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    using var previewFrame = window.CaptureRenderedFrame();
                    Assert.That(previewFrame, Is.Not.Null, $"{language}/{theme}: context preview frame");
                    previewFrame!.Save(Path.Combine(evidenceDirectory, $"context-preview-{language}-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
                Avalonia.Application.Current.RequestedThemeVariant = previousTheme;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                await tab.SendAiChatCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.That(tab.ChatStatus, Is.EqualTo(AiChatStatus.Ready));
                Assert.That(tab.Text, Is.EqualTo("db.customers.find({})"));
                Assert.That(tab.ChatStatusMessage, Is.EqualTo(expectedReady));
                AssertLocalizedPanel(view, LocalizationViewModel.Current.Resolve("proposalToReview"));

                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Avalonia.Application.Current.RequestedThemeVariant = theme;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.That(view.Bounds.Width, Is.GreaterThan(700), $"{language}/{theme}: assistant panel width");
                    Assert.That(VisibleButtons(view).Any(button =>
                        button.Content?.ToString() == LocalizationViewModel.Current.Resolve("applyProposal")), Is.True,
                        $"{language}/{theme}: apply action visible");
                    using var frame = window.CaptureRenderedFrame();
                    Assert.That(frame, Is.Not.Null, $"{language}/{theme}: frame");
                    frame!.Save(Path.Combine(evidenceDirectory, $"assistant-{language}-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }

                ClickButton(view, LocalizationViewModel.Current.Resolve("applyProposal"));
                Dispatcher.UIThread.RunJobs();
                Assert.That(tab.Text, Is.EqualTo("db.customers.find({ ativo: true })"), "Aplicar insere a proposta revisada no editor.");
                var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
                editor.Focus();
                window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
                Assert.That(tab.Text, Is.EqualTo("db.customers.find({})"), "Uma operação de undo restaura o texto original.");
                tab.Text = "db.customers.find({})";

                // A destructive proposal asks for the second confirmation; rejecting it keeps editor text intact.
                chat.Responder = (_, _) => Task.FromResult<AiChatResponse?>(new AiChatResponse(
                    "Remove clientes.", "db.customers.deleteMany({})", "+ deleteMany({})", true, "Operação destrutiva."));
                tab.ChatInput = "Remova os clientes";
                await tab.SendAiChatCommand.ExecuteAsync(null);
                await tab.SendAiChatCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.That(tab.AiProposal?.RequiresAdditionalConfirmation, Is.True);
                Assert.That(tab.ChatStatusMessage, Is.EqualTo(LocalizationViewModel.Current.Resolve("aiProposalReadyConfirm")));
                AssertLocalizedPanel(view, LocalizationViewModel.Current.Resolve("proposalToReview"));
                ClickButton(view, LocalizationViewModel.Current.Resolve("applyProposal"));
                Dispatcher.UIThread.RunJobs();
                var confirmation = window.OwnedWindows.OfType<Window>().Single();
                var reject = confirmation.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content?.ToString() == LocalizationViewModel.Current.Resolve("cancel"));
                reject.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                await Task.Yield();
                Assert.That(tab.Text, Is.EqualTo("db.customers.find({})"), "rejeitar confirmação não altera editor");
                Assert.That(tab.AiProposal, Is.Not.Null, "a proposta continua disponível após negar a confirmação");

                // A stale proposal is rejected by the actual Apply button when the editor changes.
                chat.Responder = (_, _) => Task.FromResult<AiChatResponse?>(new AiChatResponse(
                    "Filtra clientes ativos.", "db.customers.find({ ativo: true })", "+ ativo: true", false, ""));
                tab.ChatInput = "Filtre clientes ativos";
                await tab.SendAiChatCommand.ExecuteAsync(null);
                await tab.SendAiChatCommand.ExecuteAsync(null);
                tab.Text = "db.customers.find({ vip: true })";
                ClickButton(view, LocalizationViewModel.Current.Resolve("applyProposal"));
                Dispatcher.UIThread.RunJobs();
                Assert.That(tab.Text, Is.EqualTo("db.customers.find({ vip: true })"));
                Assert.That(tab.AiProposal, Is.Null);
                AssertLocalizedPanel(view, LocalizationViewModel.Current.Resolve("proposalStaleEditorTarget"));
            }
            finally
            {
                window.Close();
                LocalizationViewModel.Current.Language = previousLanguage;
                Avalonia.Application.Current.RequestedThemeVariant = previousTheme;
            }

            return true;
        }, CancellationToken.None);
    }

    private static IEnumerable<Button> VisibleButtons(WorkspaceTabView view) => view.GetVisualDescendants().OfType<Button>()
        .Where(button => button.IsEffectivelyVisible && button.Bounds.Width > 0 && button.Bounds.Height > 0);

    private static void ClickButton(WorkspaceTabView view, string content)
    {
        var button = VisibleButtons(view).Single(candidate => candidate.Content?.ToString() == content);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private static void AssertLocalizedPanel(WorkspaceTabView view, string expected)
    {
        var visibleText = string.Join("\n", view.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible).Select(block => block.Text));
        Assert.That(visibleText, Does.Contain(expected));
        Assert.That(visibleText, Does.Not.Contain("[["), "A UI não deve exibir chaves de localização ausentes.");
    }

    private sealed class ScriptedChatService : IAiChatService
    {
        private TaskCompletionSource? _entered;
        private bool _blockNext;
        public int RequestCount { get; private set; }
        public TaskCompletionSource RequestEntered => _entered ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<AiChatRequest, CancellationToken, Task<AiChatResponse?>> Responder { get; set; } =
            (_, _) => Task.FromResult<AiChatResponse?>(null);

        public void BlockNextRequest()
        {
            _blockNext = true;
            _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task<AiChatResponse?> AskAsync(AiChatRequest request, CancellationToken cancellationToken = default)
        {
            RequestCount++;
            if (_blockNext)
            {
                _blockNext = false;
                _entered!.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return await Responder(request, cancellationToken);
        }
    }
}
