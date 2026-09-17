using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LocalAiModelServiceTests
{
    private static readonly string[] SwitchLog = ["load Coder-0.5B", "generate Coder-0.5B", "generate Coder-0.5B", "unload Coder-0.5B", "load Coder-1.5B", "generate Coder-1.5B",
        "unload Coder-1.5B", "load Coder-Chat", "generate Coder-Chat"];
    private static readonly string[] TestSteps = ["Pasta e arquivos", "Tokenizer", "Sessão ONNX e provider", "Geração"];
    private static readonly AiChatRequest ChatRequest = new(new("Limitar a 10", "Console", "db.customers.find({})", "javascript", "MongoDB", "test", "customers", "consulta"));

    [Test]
    public async Task SwitchingModelsReleasesThePreviousModelBeforeLoadingTheNext()
    {
        var log = new List<string>();
        await using var service = new LocalAiModelService(new FolderCatalogFake(), () => new RecordingRuntime(log));
        var small = new AutocompleteSettings { SelectedModel = "Coder-0.5B" };
        await service.GenerateAsync(LocalModelRole.Autocomplete, small, Request, AiRequestPriority.Background);
        // Budgets and refreshing the catalog do not change the model identity.
        await service.SwitchModelAsync(small with { ContextTokens = 1024, DelayMilliseconds = 300 });
        await service.DiscoverModelsAsync();
        Assert.That(service.LoadedModel?.Name, Is.EqualTo("Coder-0.5B"));
        await service.GenerateAsync(LocalModelRole.Autocomplete, small, Request, AiRequestPriority.Background);
        var large = small with { SelectedModel = "Coder-1.5B" };
        await service.SwitchModelAsync(large);
        Assert.That(service.LoadedModel, Is.Null);
        Assert.That(service.Status.State, Is.EqualTo(LocalModelState.NotLoaded));
        await service.GenerateAsync(LocalModelRole.Autocomplete, large, Request, AiRequestPriority.Background);
        await service.GenerateAsync(LocalModelRole.Chat, large with { ChatModel = "Coder-Chat" }, Request, AiRequestPriority.Interactive);
        Assert.That(log, Is.EqualTo(SwitchLog));
        Assert.That(service.GetCapabilities().HasFlag(LocalModelCapabilities.Chat), Is.True);
    }

    [Test]
    public async Task ChatIsUnavailableForAModelThatDoesNotDeclareIt()
    {
        var runtime = new CompletionRuntimeFake();
        var catalog = new CompletionCatalogFake
        {
            Validation = new(new("fim", "FIM only", "models", "Qwen2.5-Coder") { Capabilities = LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim },
                new(LocalModelState.Available, "available"))
        };
        await using var provider = new AiAutocompleteProvider(catalog, () => runtime);
        var autocomplete = new AutocompleteService(provider);
        await autocomplete.ConfigureAsync(new() { ModelPath = "model" });
        Assert.That((await autocomplete.GetCompletionAsync(new("db.", "")))?.IsAi, Is.True);
        var error = Assert.ThrowsAsync<LocalModelUnavailableException>(() => new LocalModelAiChatService(provider, autocomplete).AskAsync(ChatRequest))!;
        Assert.That(error.Message, Does.Contain("FIM only").And.Contain("capacidade chat"));
        Assert.That(runtime.Generations, Is.EqualTo(1));
        Assert.That(runtime.Disposed, Is.False, "A missing capability is not a model failure.");
    }

    [Test]
    public async Task ExplicitChatPreemptsRunningAutocompleteWithoutReloading()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CompletionRuntimeFake
        {
            Handler = async (request, token) =>
            {
                if (!request.RequireFullContext) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
                return new("db.customers.find({}).limit(10)", 8, TimeSpan.FromMilliseconds(5), "cpu");
            }
        };
        await using var provider = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var autocomplete = new AutocompleteService(provider);
        await autocomplete.ConfigureAsync(new() { ModelPath = "model" });
        var background = autocomplete.GetCompletionAsync(new("db.", ""));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var response = await new LocalModelAiChatService(provider, autocomplete).AskAsync(ChatRequest).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(response!.ProposedCode, Is.EqualTo("db.customers.find({}).limit(10)"));
        Assert.That(await background.WaitAsync(TimeSpan.FromSeconds(5)), Is.Null, "The editor shows no suggestion; it does not fail.");
        Assert.That(runtime.Initializations, Is.EqualTo(1));
        Assert.That(runtime.Disposed, Is.False);
    }

    [Test]
    public async Task CancelingTheRequestingEditorDoesNotAbortTheModelLoad()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CompletionRuntimeFake { OnInitialize = _ => { started.TrySetResult(); return release.Task; } };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        using var typing = new CancellationTokenSource();
        var first = service.GenerateAsync(LocalModelRole.Autocomplete, new(), Request, AiRequestPriority.Background, typing.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        typing.Cancel();
        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(service.Status.State, Is.EqualTo(LocalModelState.Loading));
        release.SetResult();
        var next = await service.GenerateAsync(LocalModelRole.Autocomplete, new(), Request, AiRequestPriority.Background).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(next.Result.Text, Is.Not.Empty);
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    [Test]
    public async Task LoadingIsReportedInTheGlobalActivityBar()
    {
        var operations = new ApplicationOperationService();
        var descriptions = new List<string>();
        operations.Changed += (_, _) => { if (operations.ActiveOperations is [var current, ..]) descriptions.Add(current.Description); };
        var runtime = new CompletionRuntimeFake { RuntimeInfo = new(AiAccelerationMode.Gpu, "DirectML", "Test GPU", TimeSpan.FromMilliseconds(842)) };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, operations: operations);
        await service.LoadModelAsync(LocalModelRole.Autocomplete, new());
        Assert.That(descriptions, Has.Some.StartsWith("Validando modelo"));
        Assert.That(descriptions, Has.Some.EqualTo("Carregando Qwen Coder…"));
        Assert.That(operations.ActiveOperations, Is.Empty);
        Assert.That(operations.LastCompleted!.Status, Is.EqualTo(ApplicationOperationStatus.Success));
        Assert.That(operations.LastCompleted.Description, Is.EqualTo("Modelo carregado — GPU"));
        Assert.That(service.Status.Backend, Is.EqualTo(AiAccelerationMode.Gpu));
        Assert.That(service.Status.Device, Is.EqualTo("Test GPU"));
    }

    [Test]
    public async Task ModelTestRunsEveryStepAndReportsMeasuredMetrics()
    {
        string? prompt = null;
        var runtime = new CompletionRuntimeFake
        {
            RuntimeInfo = new(AiAccelerationMode.Gpu, "DirectML", "Test GPU", TimeSpan.FromMilliseconds(842)),
            Handler = (request, _) =>
            {
                prompt = request.Prefix;
                return Task.FromResult(new ModelGenerationResult("})", 9, TimeSpan.FromMilliseconds(265), "dml") { TimeToFirstToken = TimeSpan.FromMilliseconds(65) });
            }
        };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        await service.GenerateAsync(LocalModelRole.Autocomplete, new() { SelectedModel = "Qwen" }, Request, AiRequestPriority.Background);
        var report = await service.TestModelAsync(new() { SelectedModel = "Qwen", Acceleration = AiAccelerationMode.Gpu });
        Assert.That(report.Succeeded, Is.True);
        Assert.That(prompt, Does.Contain("db.Users.find({"));
        Assert.That(runtime.Initializations, Is.EqualTo(2), "The test reloads instead of reusing the previous session.");
        Assert.That(report.Steps.Select(step => step.Name), Is.EqualTo(TestSteps));
        Assert.That(report.TokensPerSecond, Is.EqualTo(40).Within(0.001));
        var text = LocalAiStatusFormatter.FormatReport(report);
        Assert.That(text, Does.StartWith("Modelo carregado com sucesso.\nModelo: Qwen Coder\nHardware: GPU\nProvider: DirectML\nDispositivo: Test GPU\n"));
        Assert.That(text, Does.Contain("Carregamento: 842 ms\nPrimeiro token: 65 ms\nGeração: 40 tokens/s"));
    }

    [Test]
    public async Task ExplicitProviderFailureIsReportedWithoutGeneration()
    {
        var runtime = new CompletionRuntimeFake { OnInitialize = _ => throw new AiProviderUnavailableException(AiAccelerationMode.Gpu, "DirectML provider unavailable.") };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var report = await service.TestModelAsync(new() { SelectedModel = "Qwen", Acceleration = AiAccelerationMode.Gpu });
        Assert.That(report.Succeeded, Is.False);
        Assert.That(report.Message, Does.StartWith("Não foi possível executar este modelo utilizando GPU.").And.Contain("Automático ou CPU"));
        Assert.That(report.Steps[^1], Is.EqualTo(new LocalModelTestStep("Sessão ONNX e provider", false, "DirectML provider unavailable.")));
        Assert.That(runtime.Generations, Is.Zero);
        Assert.That(runtime.Disposed, Is.True);
        Assert.That(service.Status.State, Is.EqualTo(LocalModelState.Failed));
    }

    [Test, Explicit("Consulta o ONNX Runtime nativo desta máquina."), Category("LocalModelIntegration")]
    public void RealHardwareProbeAlwaysReportsCpu()
    {
        var devices = OnnxHardwareProbe.Detect();
        foreach (var device in devices) TestContext.WriteLine(LocalAiStatusFormatter.DeviceLine(device) + (device.Reason is null ? "" : " · " + device.Reason));
        Assert.That(devices.Single(device => device.Kind == AiAccelerationMode.Cpu).IsAvailable, Is.True);
    }

    [TestCase(AiAccelerationMode.Cpu)]
    [TestCase(AiAccelerationMode.Gpu)]
    [TestCase(AiAccelerationMode.Auto)]
    [Explicit("Defina SLOP_QWEN_MODEL para uma pasta de modelo ONNX GenAI externa."), Category("LocalModelIntegration")]
    public async Task RealModelTestRunsOnTheRequestedHardware(AiAccelerationMode hardware)
    {
        var path = Environment.GetEnvironmentVariable("SLOP_QWEN_MODEL");
        Assert.That(path, Is.Not.Null.And.Not.Empty);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path!));
        var probe = new OnnxHardwareProbe();
        await using var service = new LocalAiModelService(new LocalModelCatalog(Path.GetDirectoryName(root)), () => new OnnxLocalModelRuntime(hardware: probe), probe);
        var report = await service.TestModelAsync(new() { SelectedModel = Path.GetFileName(root), Acceleration = hardware });
        TestContext.WriteLine(LocalAiStatusFormatter.FormatReport(report));
        Assert.That(report.Succeeded, Is.True, report.Message);
        if (hardware != AiAccelerationMode.Auto) Assert.That(report.Backend, Is.EqualTo(hardware));
    }

    private static ModelGenerationRequest Request(LocalModelDefinition model) => new("db.", "", 512, 8);

    private sealed class FolderCatalogFake : ILocalModelCatalog
    {
        public string DefaultDirectory => "models";
        public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalModelValidation>>([]);
        public Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelValidation(new(Path.GetFileName(path), Path.GetFileName(path), path, "Qwen2.5-Coder"), new(LocalModelState.Available, "available")));
    }

    private sealed class RecordingRuntime(List<string> log) : ILocalModelRuntime
    {
        private string _name = "";
        public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default)
        {
            _name = model.Name;
            log.Add("load " + _name);
            return Task.CompletedTask;
        }

        public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default)
        {
            log.Add("generate " + _name);
            return Task.FromResult(new ModelGenerationResult("find({})", 3, TimeSpan.FromMilliseconds(3), "cpu"));
        }

        public ValueTask DisposeAsync()
        {
            log.Add("unload " + _name);
            return ValueTask.CompletedTask;
        }
    }
}
