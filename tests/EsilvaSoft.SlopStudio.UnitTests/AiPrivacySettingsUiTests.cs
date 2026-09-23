using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Desktop;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AiPrivacySettingsUiTests
{
    [Test]
    public async Task LocalAiContextConsentRendersInLightAndDarkThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, new CompletionServiceFake());
            var window = new AutocompleteSettingsWindow { DataContext = workspace.AutocompletePreferences, Width = 660, Height = 680 };
            window.Show();
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.That(window.FindControl<CheckBox>("AllowLocalAiContext"), Is.Not.Null);
                Assert.That(window.FindControl<CheckBox>("IncludeLocalAiInputJson")?.IsEnabled, Is.False,
                    "Input JSON opt-in stays unavailable until global local-AI context consent is enabled.");
                Save(window, $"local-ai-context-settings-{theme}.png");
            }
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task PerConnectionLocalAiOptOutRendersInLightAndDarkThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var viewModel = new ConnectionsViewModel(context.Workspace);
            viewModel.Editor.ShowProfileEditorCommand.Execute(null);
            var window = new ConnectionsWindow { DataContext = viewModel, Width = 800, Height = 560 };
            window.Show();
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                var input = window.FindControl<CheckBox>("AllowLocalAiConnectionContext")!;
                var editorScroll = input.GetVisualAncestors().OfType<ScrollViewer>().First();
                editorScroll.Offset = new Vector(0, Math.Max(0, editorScroll.Extent.Height - editorScroll.Viewport.Height));
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.That(viewModel.Editor.NewProfileLocalAiContextEnabled, Is.True);
                Assert.That(window.FindControl<CheckBox>("AllowLocalAiConnectionContext")?.Bounds.Height, Is.GreaterThan(0));
                Save(window, $"local-ai-connection-context-{theme}.png");
            }
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static void Save(Window window, string fileName)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, fileName), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
