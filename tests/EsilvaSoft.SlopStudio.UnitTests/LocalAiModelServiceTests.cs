using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LocalAiModelServiceTests
{
    private static readonly AiHardwareDevice Cpu = new(AiAccelerationMode.Cpu, "CPU", "Test CPU", true);
    private static readonly AiHardwareDevice Gpu = new(AiAccelerationMode.Gpu, "DirectML", "Test GPU", true);
    private static readonly AiHardwareDevice MissingGpu = new(AiAccelerationMode.Gpu, "", "GPU", false) { Reason = "DirectML provider unavailable." };
    private static readonly AiHardwareDevice Npu = new(AiAccelerationMode.Npu, "QNN", "Test NPU", true);
    private static readonly AiHardwareDevice MissingNpu = new(AiAccelerationMode.Npu, "", "NPU", false);
    private static readonly AiAccelerationMode[] AutomaticOrder = [AiAccelerationMode.Npu, AiAccelerationMode.Gpu, AiAccelerationMode.Cpu];
    private static readonly AiAccelerationMode[] CpuOnly = [AiAccelerationMode.Cpu];
    private static readonly string[] DiscoveredFolders = ["Broken-Metadata", "Empty", "Llama-Plain", "SlopCoder-Mongo-0.5B", "SlopCoder-Mongo-1.5B", "SlopCoder-Test"];
    private static readonly string[] SwitchLog = ["load Coder-0.5B", "generate Coder-0.5B", "generate Coder-0.5B", "unload Coder-0.5B", "load Coder-1.5B", "generate Coder-1.5B",
        "unload Coder-1.5B", "load Coder-Chat", "generate Coder-Chat"];
    private static readonly string[] TestSteps = ["Pasta e arquivos", "Tokenizer", "Sessão ONNX e provider", "Geração"];
    private static readonly string[] ProbeLines = ["CPU — Ryzen", "GPU — RX 7800 XT (DirectML, 15,8 GB)", "NPU — indisponível"];
    private static readonly string[] IdleStatusLines = ["Modelo selecionado: SlopCoder-Mongo-1.5B", "Estado: Não carregado", "Hardware: Automático",
        "Provider detectado: DirectML", "Dispositivo: Test GPU", "Modelo ainda não carregado; será validado sob demanda."];
    private static readonly string[] TwoModels = ["Coder-0.5B", "Coder 1.5B"];
    private static readonly string[] RefreshedModels = ["Coder 1.5B", "Coder-3B"];
    private static readonly AiChatRequest ChatRequest = new(new("Limitar a 10", "Console", "db.customers.find({})", "javascript", "MongoDB", "test", "customers", "consulta"));

    [Test]
    public async Task DiscoveryUsesFolderNamesOptionalMetadataAndIsolatesInvalidFolders()
    {
        using var models = new TemporaryDirectory();
        CreateQwenModel(models.Path, "SlopCoder-Mongo-0.5B");
        CreateQwenModel(models.Path, "SlopCoder-Mongo-1.5B", """
            {"name":"SlopCoder Mongo 1.5B","version":"1.0.0","parameters":"1.5B","domain":["mongodb","json"],"capabilities":["autocomplete","fim","future"],
             "hardware":["cpu"],"recommendedContextTokens":2048,"recommendedCompletionTokens":128,"generation":{"chat":{"maxTokens":512,"temperature":0.3}}}
            """);
        File.Delete(Path.Combine(CreateQwenModel(models.Path, "SlopCoder-Test"), "tokenizer.json"));
        CreateQwenModel(models.Path, "Broken-Metadata", "{\"capabilities\":\"chat\"}");
        File.WriteAllText(Path.Combine(CreateQwenModel(models.Path, "Llama-Plain"), "genai_config.json"), "{\"model\":{\"type\":\"llama\",\"decoder\":{\"filename\":\"model.onnx\"}}}");
        Directory.CreateDirectory(Path.Combine(models.Path, "Empty"));

        var found = (await new LocalModelCatalog(models.Path).DiscoverAsync()).ToDictionary(validation => Path.GetFileName(validation.Path));

        Assert.That(found.Keys, Is.EqualTo(DiscoveredFolders));
        Assert.That(found["SlopCoder-Mongo-0.5B"].Model!.Name, Is.EqualTo("SlopCoder-Mongo-0.5B"));
        Assert.That(found["SlopCoder-Mongo-0.5B"].Model!.Capabilities.HasFlag(LocalModelCapabilities.Chat), Is.True);
        var described = found["SlopCoder-Mongo-1.5B"].Model!;
        Assert.That(described.Id, Is.EqualTo("SlopCoder-Mongo-1.5B"));
        Assert.That(described.Name, Is.EqualTo("SlopCoder Mongo 1.5B"));
        Assert.That(described.Capabilities, Is.EqualTo(LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim));
        Assert.That(described.Metadata!.Hardware, Is.EqualTo(CpuOnly));
        Assert.That(described.Metadata.Chat, Is.EqualTo(new LocalModelGenerationDefaults(512, 0.3)));
        Assert.That(found["SlopCoder-Test"].Validity, Is.EqualTo(LocalModelValidity.MissingFiles));
        Assert.That(found["SlopCoder-Test"].Status.Message, Does.Contain("tokenizer.json"));
        Assert.That(found["Empty"].Validity, Is.EqualTo(LocalModelValidity.MissingFiles));
        Assert.That(found["Broken-Metadata"].Validity, Is.EqualTo(LocalModelValidity.Invalid));
        Assert.That(found["Llama-Plain"].Validity, Is.EqualTo(LocalModelValidity.Unsupported));
    }

    [TestCase("..")]
    [TestCase("models/other")]
    [TestCase(@"models\other")]
    [TestCase(@"C:\models\other")]
    [TestCase(" padded")]
    public void SelectedModelIsAFolderNameThatCannotEscapeTheDirectory(string name)
    {
        Assert.Throws<ArgumentException>(() => new AutocompleteSettings { SelectedModel = name }.Validate());
        Assert.Throws<ArgumentException>(() => new AutocompleteSettings { ChatModel = name }.Validate());
    }

    [Test]
    public void SelectionResolvesInsideTheDirectoryAndChatCanUseItsOwnModel()
    {
        var settings = new AutocompleteSettings { ModelDirectory = "models", SelectedModel = "Coder-0.5B", ChatModel = "Coder-1.5B", ModelPath = "external" }.Validate();
        Assert.That(settings.ResolveModelPath(LocalModelRole.Autocomplete, "default"), Is.EqualTo(Path.Combine("models", "Coder-0.5B")));
        Assert.That(settings.ResolveModelPath(LocalModelRole.Chat, "default"), Is.EqualTo(Path.Combine("models", "Coder-1.5B")));
        Assert.That((settings with { ModelDirectory = "" }).ResolveModelPath(LocalModelRole.Autocomplete, "default"), Is.EqualTo(Path.Combine("default", "Coder-0.5B")));
        Assert.That((settings with { SelectedModel = "", ChatModel = "" }).ResolveModelPath(LocalModelRole.Chat, "default"), Is.EqualTo("external"));
        Assert.That(new AutocompleteSettings().HasModelSelection(), Is.False);
    }

    [Test]
    public void AutomaticTriesNpuGpuThenCpuAmongAvailableAndCompatibleDevices()
    {
        var automatic = AiProviderSelector.Plan(new(), [Cpu, Gpu, Npu]);
        Assert.That(automatic.AllowFallback, Is.True);
        Assert.That(automatic.Candidates.Select(candidate => candidate.Kind), Is.EqualTo(AutomaticOrder));
        Assert.That(AiProviderSelector.Plan(new(), [Cpu, MissingGpu, MissingNpu]).Candidates.Single().GenAiName, Is.EqualTo("cpu"));
        Assert.That(AiProviderSelector.Plan(new(), [Cpu, Gpu, Npu], CpuOnlyModel()).Candidates.Select(candidate => candidate.Kind), Is.EqualTo(CpuOnly));
    }

    [Test]
    public void ExplicitHardwareUsesOnlyThatBackendAndExplainsUnavailability()
    {
        var gpu = AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Gpu }, [Cpu, Gpu]);
        Assert.That(gpu.AllowFallback, Is.False);
        Assert.That(gpu.Candidates.Single(), Is.EqualTo(new AiProviderCandidate(AiAccelerationMode.Gpu, "DirectML", "dml", "Test GPU")));
        var unavailable = Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Gpu }, [Cpu, MissingGpu]))!;
        Assert.That(unavailable.Message, Is.EqualTo("Não foi possível executar este modelo utilizando GPU.\nMotivo: DirectML provider unavailable.\nVocê pode selecionar: Automático ou CPU."));
        Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Npu }, [Cpu, Gpu, MissingNpu]));
        Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { ExecutionProvider = AiExecutionProvider.Cuda }, [Cpu, Gpu]));
        Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Gpu }, [Cpu, Gpu], CpuOnlyModel()));
    }

    [Test]
    public void ProbeOffersOnlyProvidersTheRuntimeReportsAndPrefersTheFirstAdapter()
    {
        var devices = OnnxHardwareProbe.Describe(["DmlExecutionProvider", "CPUExecutionProvider"],
        [
            new("CPUExecutionProvider", AiAccelerationMode.Cpu, "Ryzen"),
            new("DmlExecutionProvider", AiAccelerationMode.Gpu, "Integrated", 485L << 20, 1),
            new("DmlExecutionProvider", AiAccelerationMode.Gpu, "RX 7800 XT", 16177L << 20, 0)
        ]);
        Assert.That(devices.Select(LocalAiStatusFormatter.DeviceLine), Is.EqualTo(ProbeLines));
        var cpuBuild = OnnxHardwareProbe.Describe(["CPUExecutionProvider"], []);
        Assert.That(cpuBuild.Single(device => device.Kind == AiAccelerationMode.Cpu).IsAvailable, Is.True);
        Assert.That(cpuBuild.Single(device => device.Kind == AiAccelerationMode.Gpu).Reason, Does.Contain("DirectML ou CUDA"));
    }

    [Test]
    public void StatusBeforeLoadingNamesSelectionStateHardwareAndDetectedProvider()
    {
        var text = LocalAiStatusFormatter.Format(new(LocalModelState.NotLoaded, "Modelo ainda não carregado; será validado sob demanda."),
            "SlopCoder-Mongo-1.5B", AiAccelerationMode.Auto, [Cpu, Gpu, MissingNpu]);
        Assert.That(text.Split('\n'), Is.EqualTo(IdleStatusLines));
    }

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

    [Test]
    public async Task PreferencesStoreFolderNamesAndRefreshKeepsTheSelection()
    {
        using var models = new TemporaryDirectory();
        CreateQwenModel(models.Path, "Coder-0.5B");
        CreateQwenModel(models.Path, "Coder-1.5B", "{\"name\":\"Coder 1.5B\",\"recommendedContextTokens\":1024,\"recommendedCompletionTokens\":64}");
        AutocompleteSettings? saved = null;
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), new LocalModelCatalog(models.Path), settings => { saved = settings; return Task.CompletedTask; });
        preferences.Load(new());
        await preferences.RefreshModelsCommand.ExecuteAsync(null);
        Assert.That(preferences.Models.Select(option => option.Display), Is.EqualTo(TwoModels));
        preferences.SelectedModelOption = preferences.Models.Single(option => option.Reference == "Coder-1.5B");
        Assert.That((preferences.ContextTokens, preferences.MaximumTokens), Is.EqualTo((1024, 64)));
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That((saved!.SelectedModel, saved.ModelPath, saved.ModelDirectory), Is.EqualTo(("Coder-1.5B", "", "")));

        Directory.Delete(Path.Combine(models.Path, "Coder-0.5B"), true);
        CreateQwenModel(models.Path, "Coder-3B");
        await preferences.RefreshModelsCommand.ExecuteAsync(null);
        Assert.That(preferences.Models.Select(option => option.Display), Is.EqualTo(RefreshedModels));
        Assert.That(preferences.SelectedModelOption!.Reference, Is.EqualTo("Coder-1.5B"));
        Assert.That(preferences.ContextTokens, Is.EqualTo(1024));

        await preferences.SelectExternalModelAsync(Path.Combine(models.Path, "Coder-3B"));
        await preferences.ApplyCommand.ExecuteAsync(null);
        Assert.That((saved.SelectedModel, saved.ModelPath), Is.EqualTo(("Coder-3B", "")), "A folder inside the directory is not stored as an absolute path.");
        preferences.Load(saved);
        Assert.That(preferences.SelectedModelOption!.Reference, Is.EqualTo("Coder-3B"));
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

    private static LocalModelDefinition CpuOnlyModel() =>
        new("cpu-only", "CPU only", "models", "Qwen2.5-Coder") { Metadata = new() { Hardware = CpuOnly } };

    private static string CreateQwenModel(string directory, string folder, string? metadata = null)
    {
        var root = Path.Combine(directory, folder);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "genai_config.json"), "{\"model\":{\"type\":\"qwen2\",\"context_length\":4096,\"decoder\":{\"filename\":\"model.onnx\"}}}");
        File.WriteAllText(Path.Combine(root, "model.onnx"), "synthetic");
        File.WriteAllText(Path.Combine(root, "tokenizer_config.json"), "{}");
        File.WriteAllText(Path.Combine(root, "tokenizer.json"), JsonSerializer.Serialize(new
        {
            added_tokens = QwenFimPromptBuilder.SpecialTokens.Select((token, index) => new { content = token, id = 151659 + index })
        }));
        if (metadata is not null) File.WriteAllText(Path.Combine(root, LocalModelMetadata.FileName), metadata);
        return root;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "models-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }

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
