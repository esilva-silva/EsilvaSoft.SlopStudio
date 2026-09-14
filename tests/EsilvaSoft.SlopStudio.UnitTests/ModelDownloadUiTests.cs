using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class ModelDownloadUiTests
{
    [Test]
    public async Task PreferencesListPublishedModelsDownloadWithProgressSelectAndCancelFromStatusBar()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var models = Path.Combine(Path.GetTempPath(), "slop-models-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(models);
            try
            {
                var remote = new ControlledRemoteModels();
                var operations = new ApplicationOperationService();
                var preferences = new AutocompleteSettingsViewModel(new AutocompleteService(), new LocalModelCatalog(models), _ => Task.CompletedTask,
                    remote: remote, operations: operations);
                preferences.Load(new AutocompleteSettings { ModelDirectory = models });
                var window = new AutocompleteSettingsWindow { DataContext = preferences };
                window.Show();
                await WaitUntil(() => preferences.RemoteModels.Count == 2);
                Assert.That(preferences.SelectedRemoteModel?.Variant.Variant, Is.EqualTo("int4"), "The first model not installed yet is preselected.");
                Assert.That(preferences.RemoteModels[1].Display, Does.StartWith("int8").And.Contain("GB"));
                Assert.That(preferences.RemoteModelDetails, Does.Contain(Path.Combine(models, "SlopCoder-Mongo-0.5B-ONNX-int4")));

                preferences.SelectedRemoteModel = preferences.RemoteModels[1];
                var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
                var download = buttons.Single(button => AutomationProperties.GetName(button) == "Baixar modelo");
                Assert.That(download.Command!.CanExecute(null), Is.True);
                download.Command.Execute(null);
                await WaitUntil(() => preferences.DownloadProgress >= 50);
                Assert.That(preferences.IsDownloadingModel, Is.True);
                Assert.That(operations.ActiveOperations.Single().Description, Does.Contain("int8"), "The status bar shows the download.");
                var cancel = buttons.Single(button => AutomationProperties.GetName(button) == "Cancelar download do modelo");
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.That(cancel.IsVisible, Is.True);
                Assert.That(download.IsVisible, Is.False);
                Render(window, cancel);

                remote.Gate.SetResult();
                await WaitUntil(() => !preferences.IsDownloadingModel && preferences.SelectedModelOption is not null);
                Assert.That(preferences.SelectedModelOption!.Reference, Is.EqualTo("SlopCoder-Mongo-0.5B-ONNX-int8"), "The installed model is selected for saving.");
                Assert.That(preferences.SelectedModelOption.Model, Is.Not.Null);
                Assert.That(preferences.DownloadStatus, Does.Contain("Salvar"));
                Assert.That(preferences.RemoteModels[1].IsInstalled, Is.True);
                Assert.That(preferences.DownloadModelCommand.CanExecute(null), Is.False, "An installed model is not downloaded again.");
                Assert.That(operations.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Success));

                remote.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                preferences.SelectedRemoteModel = preferences.RemoteModels[0];
                var second = preferences.DownloadModelCommand.ExecuteAsync(null);
                await WaitUntil(() => operations.ActiveOperations.Count == 1);
                operations.Cancel(operations.ActiveOperations[0].Id);
                await second;
                Assert.That(preferences.DownloadStatus, Does.Contain("cancelado"));
                Assert.That(preferences.RemoteModels[0].IsInstalled, Is.False);
                Assert.That(operations.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Cancelled));
                window.Close();
            }
            finally
            {
                try { Directory.Delete(models, true); }
                catch (IOException) { }
            }
            return true;
        }, CancellationToken.None);
    }

    private static void Render(AutocompleteSettingsWindow window, Button cancel)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence");
        Directory.CreateDirectory(directory);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in new[] { new Size(520, 420), new Size(660, 680), new Size(900, 760) })
                foreach (var scale in new[] { 1d, 1.5, 2 })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    cancel.BringIntoView();
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var right = cancel.TranslatePoint(new Point(cancel.Bounds.Width, 0), window)!.Value.X;
                    Assert.That(right, Is.LessThanOrEqualTo(window.ClientSize.Width + 0.5), $"{size}: download actions overflow");
                    Assert.That(cancel.Bounds.Height, Is.GreaterThanOrEqualTo(28));
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"model-download-{theme}-{size.Width}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"),
                        new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.That(condition(), Is.True, "Timed out waiting for the download state.");
    }
}

internal sealed class ControlledRemoteModels : IRemoteModelSource
{
    public Uri RepositoryUrl { get; } = new("https://huggingface.co/esilva/SlopCoder-Mongo-0.5B-ONNX");
    public TaskCompletionSource Gate { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IReadOnlyList<RemoteModelVariant>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RemoteModelVariant>>([Variant("int4", 415_000_000), Variant("int8", 1_130_000_000)]);

    public async Task<string> DownloadAsync(RemoteModelVariant variant, string modelsDirectory, IProgress<RemoteModelProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new RemoteModelProgress(variant.SizeBytes / 2, variant.SizeBytes, "model.onnx.data"));
        await Gate.Task.WaitAsync(cancellationToken);
        // A structurally valid Qwen FIM export, so the catalog lists the installed folder.
        var path = Path.Combine(modelsDirectory, variant.FolderName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "genai_config.json"), """{"model":{"type":"qwen2","decoder":{"filename":"model.onnx"}}}""");
        File.WriteAllText(Path.Combine(path, "model.onnx"), "onnx");
        File.WriteAllText(Path.Combine(path, "tokenizer.json"), JsonSerializer.Serialize(new { added_tokens = QwenFimPromptBuilder.SpecialTokens.Select(token => new { content = token }) }));
        File.WriteAllText(Path.Combine(path, "tokenizer_config.json"), "{}");
        return path;
    }

    private RemoteModelVariant Variant(string name, long size) =>
        new("esilva/SlopCoder-Mongo-0.5B-ONNX", new string('a', 40), name, "SlopCoder-Mongo-0.5B-ONNX-" + name, size, "deepseek-license",
            new Uri($"{RepositoryUrl}/tree/main/{name}"), []);
}
