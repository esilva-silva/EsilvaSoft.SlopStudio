using System.Diagnostics;
using EsilvaSoft.SlopStudio.Application.Language;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>
/// Keeps at most one local model loaded for autocomplete and chat. Loading is lazy and detached from the requesting editor,
/// so continued typing cannot abort it; generations are serialized, prioritized and keep their own cancellation.
/// </summary>
public sealed class LocalAiModelService(ILocalModelCatalog catalog, Func<ILocalModelRuntime> runtimeFactory, IAiHardwareProbe? hardware = null,
    IAutocompleteDiagnostics? diagnostics = null, IApplicationOperationService? operations = null, TimeProvider? timeProvider = null)
    : ILocalAiModelService, IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly PriorityGate _gate = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _stateGate = new();
    // Guarded by _gate.
    private ActiveModel? _loaded;
    private Task<ActiveModel>? _loading;
    private CancellationTokenSource? _loadCancellation;
    private ModelKey? _key;
    // Guarded by _stateGate.
    private ModelKey? _failedKey;
    private DateTimeOffset _retryAfter;
    private CancellationTokenSource? _active;
    private CancellationTokenSource? _activePreemption;
    private AiRequestPriority _activePriority;
    private LocalModelDefinition? _loadedDefinition;
    private LocalModelStatus _status = new(LocalModelState.NotLoaded, "Nenhum modelo carregado; autocomplete básico disponível.");
    private bool _disposed;

    public string DefaultDirectory => catalog.DefaultDirectory;
    public LocalModelStatus Status => Volatile.Read(ref _status);
    public LocalModelDefinition? LoadedModel => Volatile.Read(ref _loadedDefinition);
    public event EventHandler? StatusChanged;

    public Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default) =>
        catalog.DiscoverAsync(string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory, cancellationToken);

    public Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default) => catalog.ValidateAsync(path, cancellationToken);

    public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default) =>
        hardware?.GetAvailableHardwareAsync(cancellationToken)
        ?? Task.FromResult<IReadOnlyList<AiHardwareDevice>>([new(AiAccelerationMode.Cpu, "CPU", "CPU", true)]);

    public LocalModelCapabilities GetCapabilities() => LoadedModel?.Capabilities ?? LocalModelCapabilities.None;

    public async Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (await EnsureLoadedAsync(KeyFor(role, settings), settings, linked.Token).ConfigureAwait(false)).Model;
        }
        finally { _gate.Release(); }
    }

    public async Task UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        CancelGeneration();
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, cancellationToken).ConfigureAwait(false);
        try
        {
            await UnloadCoreAsync().ConfigureAwait(false);
            ClearRetry();
            SetStatus(new(LocalModelState.NotLoaded, "Modelo ainda não carregado; será validado sob demanda.") { ModelName = Status.ModelName, RequestedHardware = Status.RequestedHardware });
        }
        finally { _gate.Release(); }
    }

    public async Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CancelGeneration();
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, cancellationToken).ConfigureAwait(false);
        try
        {
            var inUse = settings.Enabled && settings.Mode != AutocompleteMode.Basic;
            // Only the model identity (folder and hardware) forces a reload; token budgets and editor options do not.
            if (!inUse || _key is not null && _key != KeyFor(LocalModelRole.Autocomplete, settings) && _key != KeyFor(LocalModelRole.Chat, settings))
                await UnloadCoreAsync().ConfigureAwait(false);
            ClearRetry();
            if (_loaded is null && _loading is null) SetStatus(IdleStatus(settings));
        }
        finally { _gate.Release(); }
    }

    public void CancelGeneration()
    {
        lock (_stateGate) _active?.Cancel();
    }

    public async Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings, Func<LocalModelDefinition, ModelGenerationRequest> request,
        AiRequestPriority priority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(request);
        var roleTag = new KeyValuePair<string, object?>("role", role.ToString());
        AutocompleteMetrics.AiCompletionRequested.Add(1, roleTag);
        using var preemption = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token, preemption.Token);
        var token = linked.Token;
        if (priority == AiRequestPriority.Interactive) PreemptBackground();
        await _gate.WaitAsync((int)priority, token).ConfigureAwait(false);
        var key = KeyFor(role, settings);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            var loaded = await EnsureLoadedAsync(key, settings, token).ConfigureAwait(false);
            RequireCapability(loaded.Model, role);
            lock (_stateGate) { _active = linked; _activePreemption = preemption; _activePriority = priority; }
            if (priority == AiRequestPriority.Background && _gate.HasWaiters((int)AiRequestPriority.Interactive)) preemption.Cancel();
            var generated = await loaded.Runtime.GenerateAsync(request(loaded.Model), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            SetStatus(GenerationStatus(loaded, key, generated));
            diagnostics?.Record("ai.generation.success", role + " | " + generated.Provider, generated.Elapsed);
            RecordGeneration(roleTag, generated);
            return new(loaded.Model, generated);
        }
        catch (OperationCanceledException) when (preemption.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        {
            diagnostics?.Record("ai.generation.preempted");
            AutocompleteMetrics.AiCompletionCancelled.Add(1, roleTag, new KeyValuePair<string, object?>("reason", "preempted"));
            throw new LocalModelPreemptedException();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            diagnostics?.Record("autocomplete.canceled");
            AutocompleteMetrics.AiCompletionCancelled.Add(1, roleTag, new KeyValuePair<string, object?>("reason", "cancelled"));
            throw;
        }
        catch (ObjectDisposedException) { throw; }
        catch (AiProviderUnavailableException ex)
        {
            await FailGenerationAsync(key, ex, ex.Message).ConfigureAwait(false);
            throw;
        }
        catch (LocalModelUnavailableException) { throw; }
        catch (LocalModelContextException ex)
        {
            // The request is too large; the model stays loaded and usable for smaller requests.
            throw new LocalModelUnavailableException("O contexto completo excede a janela do modelo. Reduza o conteúdo antes de solicitar à IA.", ex);
        }
        catch (Exception ex)
        {
            AutocompleteMetrics.AiCompletionGenerated.Add(1, roleTag, new KeyValuePair<string, object?>("outcome", "failed"));
            await FailGenerationAsync(key, ex).ConfigureAwait(false);
            throw new LocalModelUnavailableException(Status.Message, ex);
        }
        finally
        {
            lock (_stateGate)
                if (ReferenceEquals(_active, linked)) { _active = null; _activePreemption = null; }
            _gate.Release();
        }
    }

    public async Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var key = KeyFor(LocalModelRole.Autocomplete, settings);
        var steps = new List<LocalModelTestStep>();
        var name = ModelName(key.Path);
        if (key.Path.Length == 0)
            return new(false, "Nenhum modelo selecionado. Escolha um modelo da lista ou uma pasta externa.", steps) { Hardware = settings.Acceleration };
        CancelGeneration();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = linked.Token;
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, token).ConfigureAwait(false);
        LocalModelTestReport Fail(string message) => new(false, message, steps.ToArray()) { ModelName = name, Hardware = settings.Acceleration };
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // A full check: never reuse a session created with previous files or hardware.
            await UnloadCoreAsync().ConfigureAwait(false);
            ClearRetry();
            var validation = await catalog.ValidateAsync(key.Path, token).ConfigureAwait(false);
            if (validation.Model is not { } model)
            {
                steps.Add(new("Pasta e arquivos", false, validation.Status.Message));
                SetStatus(validation.Status with { ModelName = name, RequestedHardware = key.Hardware });
                return Fail(validation.Status.Message);
            }
            name = model.Name;
            steps.Add(new("Pasta e arquivos", true, "genai_config.json, decoder ONNX e tokenizer encontrados."));
            ActiveModel loaded;
            try { loaded = await EnsureLoadedAsync(key, settings, token).ConfigureAwait(false); }
            catch (LocalModelUnavailableException ex) when (ex.InnerException is LocalModelLoadException { Stage: LocalModelLoadStage.Tokenizer })
            {
                steps.Add(new("Tokenizer", false, ex.Message));
                return Fail(ex.Message);
            }
            catch (LocalModelUnavailableException ex) when (!token.IsCancellationRequested)
            {
                steps.Add(new("Sessão ONNX e provider", false, ex is AiProviderUnavailableException provider ? provider.Reason : ex.Message));
                return Fail(ex.Message);
            }
            var info = loaded.Runtime.RuntimeInfo ?? loaded.Info;
            steps.Add(new("Tokenizer", true, "Carregado com o modelo."));
            steps.Add(new("Sessão ONNX e provider", true, info is null ? "Sessão criada."
                : $"{LocalAiStatusFormatter.HardwareLabel(info.Backend)} · {info.Provider}{(info.Device is null ? "" : " · " + info.Device)}"));
            var probe = new AutocompleteRequest("db.Users.find({", "", "javascript");
            ModelGenerationResult generated;
            try
            {
                generated = await loaded.Runtime.GenerateAsync(new(AutocompleteContextBuilder.ModelPrefix(probe, IsDeepSeek(loaded.Model)), "",
                    Math.Min(settings.ContextTokens, 512), 8), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var message = ex is AiProviderUnavailableException provider ? provider.Message : "A geração falhou. Confira memória, provider e exportação ONNX.";
                steps.Add(new("Geração", false, message));
                await FailGenerationAsync(key, ex, message).ConfigureAwait(false);
                return Fail(message) with { Backend = info?.Backend, Provider = info?.Provider, Device = info?.Device, LoadTime = info?.LoadTime };
            }
            info = loaded.Runtime.RuntimeInfo ?? info;
            var succeeded = generated.GeneratedTokens > 0;
            steps.Add(new("Geração", succeeded, succeeded ? $"{generated.GeneratedTokens} token(s) gerado(s)." : "Nenhum token válido: o modelo parou imediatamente."));
            SetStatus(GenerationStatus(loaded, key, generated));
            return new(succeeded, succeeded ? "Modelo carregado com sucesso." : "O modelo carregou, mas não gerou tokens válidos.", steps.ToArray())
            {
                ModelName = loaded.Model.Name, Hardware = settings.Acceleration, Backend = info?.Backend, Provider = info?.Provider, Device = info?.Device,
                LoadTime = info?.LoadTime, FirstToken = generated.TimeToFirstToken, TokensPerSecond = TokensPerSecond(generated),
                GeneratedTokens = generated.GeneratedTokens, UsedFallback = generated.UsedCpuFallback || info?.UsedFallback == true
            };
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _shutdown.Cancel();
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await UnloadCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    internal static double? TokensPerSecond(ModelGenerationResult result)
    {
        if (result.GeneratedTokens >= 2 && result.TimeToFirstToken is { } first && result.Elapsed > first)
            return (result.GeneratedTokens - 1) / (result.Elapsed - first).TotalSeconds;
        return result.GeneratedTokens > 0 && result.Elapsed > TimeSpan.Zero ? result.GeneratedTokens / result.Elapsed.TotalSeconds : null;
    }

    internal static bool IsDeepSeek(LocalModelDefinition model) => model.PromptFormat == LocalModelPromptFormats.DeepSeekCoderFim;

    // The provider tag is the execution provider name (cpu, dml, cuda); model folders and prompts are never tagged.
    private static void RecordGeneration(KeyValuePair<string, object?> role, ModelGenerationResult generated)
    {
        var provider = new KeyValuePair<string, object?>("provider", generated.Provider);
        AutocompleteMetrics.AiCompletionGenerated.Add(1, role, provider, new KeyValuePair<string, object?>("outcome", "success"));
        AutocompleteMetrics.InferenceDuration.Record(generated.Elapsed.TotalMilliseconds, role, provider);
        AutocompleteMetrics.InferenceGeneratedTokens.Record(generated.GeneratedTokens, role);
        if (generated.TimeToFirstToken is { } first) AutocompleteMetrics.InferenceTimeToFirstToken.Record(first.TotalMilliseconds, role, provider);
        if (TokensPerSecond(generated) is { } rate) AutocompleteMetrics.InferenceTokensPerSecond.Record(rate, provider);
    }

    private ModelKey KeyFor(LocalModelRole role, AutocompleteSettings settings) =>
        new(settings.ResolveModelPath(role, DefaultDirectory), settings.Acceleration, settings.ExecutionProvider);

    private static string ModelName(string path) => path.Length == 0 ? "" : Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    private static void RequireCapability(LocalModelDefinition model, LocalModelRole role)
    {
        if (role == LocalModelRole.Chat && !model.Capabilities.HasFlag(LocalModelCapabilities.Chat))
            throw new LocalModelUnavailableException($"Chat indisponível: o modelo {model.Name} não declara a capacidade chat. Selecione outro modelo para o Assistente IA.");
        if (role == LocalModelRole.Autocomplete && (model.Capabilities & (LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim)) == 0)
            throw new LocalModelUnavailableException($"Autocomplete por IA indisponível: o modelo {model.Name} não declara as capacidades autocomplete ou fim.");
    }

    private void PreemptBackground()
    {
        lock (_stateGate)
            if (_active is not null && _activePriority == AiRequestPriority.Background) _activePreemption?.Cancel();
    }

    private async Task<ActiveModel> EnsureLoadedAsync(ModelKey key, AutocompleteSettings settings, CancellationToken token)
    {
        if (_key is not null && _key != key)
        {
            diagnostics?.Record("model.switch", ModelName(key.Path));
            await UnloadCoreAsync().ConfigureAwait(false);
        }
        if (_loaded is { } loaded) return loaded;
        if (_loading is null)
        {
            lock (_stateGate)
                if (_failedKey == key && _clock.GetUtcNow() < _retryAfter) throw new LocalModelUnavailableException(Status.Message);
            _key = key;
            _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _loading = LoadCoreAsync(key, settings, _loadCancellation.Token);
        }
        var loading = _loading;
        try
        {
            var result = await loading.WaitAsync(token).ConfigureAwait(false);
            if (ReferenceEquals(_loading, loading))
            {
                _loaded = result; _loading = null;
                Volatile.Write(ref _loadedDefinition, result.Model);
            }
            return result;
        }
        catch (Exception) when (loading.IsCompleted && !loading.IsCompletedSuccessfully)
        {
            if (ReferenceEquals(_loading, loading))
            {
                _loading = null; _key = null;
                _loadCancellation?.Dispose(); _loadCancellation = null;
            }
            throw;
        }
    }

    private async Task<ActiveModel> LoadCoreAsync(ModelKey key, AutocompleteSettings settings, CancellationToken cancellationToken)
    {
        var name = ModelName(key.Path);
        using var operation = operations?.Begin($"Validando modelo {name}…", ApplicationOperationPriority.Low, canCancel: true, cancellationToken);
        var token = operation?.Token ?? cancellationToken;
        ILocalModelRuntime? runtime = null;
        SetStatus(new(LocalModelState.Loading, $"Validando modelo {name}…") { ModelName = name, RequestedHardware = key.Hardware });
        try
        {
            await Task.Yield();
            var validation = await catalog.ValidateAsync(key.Path, token).ConfigureAwait(false);
            if (validation.Model is not { } model)
            {
                MarkFailed(key, validation.Status with { ModelName = name, RequestedHardware = key.Hardware });
                operation?.Complete(ApplicationOperationStatus.Warning, $"Modelo {name} indisponível: {LocalAiStatusFormatter.ValidityLabel(validation.Validity)}");
                throw new LocalModelUnavailableException(validation.Status.Message);
            }
            name = model.Name;
            operation?.Report(0, 0, $"Carregando {name}…");
            SetStatus(new(LocalModelState.Loading, $"Carregando {name}…") { ModelName = name, RequestedHardware = key.Hardware });
            diagnostics?.Record("model.load", model.Id + " | " + model.Path);
            runtime = runtimeFactory();
            var watch = Stopwatch.StartNew();
            await runtime.InitializeAsync(model, settings, new InlineProgress(stage =>
            {
                operation?.Report(0, 0, stage);
                SetStatus(new(LocalModelState.Loading, stage) { ModelName = model.Name, RequestedHardware = key.Hardware });
            }), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var info = runtime.RuntimeInfo;
            var loaded = new ActiveModel(model, runtime, info);
            ClearRetry();
            SetStatus(LoadedStatus(loaded, key, watch.Elapsed));
            operation?.Complete(ApplicationOperationStatus.Success, info is null ? $"Modelo carregado — {name}"
                : $"Modelo carregado — {LocalAiStatusFormatter.HardwareLabel(info.Backend)}");
            diagnostics?.Record("model.ready", model.Id + " | " + (info?.Provider ?? "?"), watch.Elapsed);
            return loaded;
        }
        catch (AiProviderUnavailableException ex)
        {
            await DisposeQuietlyAsync(runtime).ConfigureAwait(false);
            diagnostics?.Record("provider.unavailable", LocalAiStatusFormatter.HardwareLabel(ex.Hardware));
            MarkFailed(key, new(LocalModelState.Failed, ex.Message) { ModelName = name, RequestedHardware = key.Hardware });
            operation?.Complete(ApplicationOperationStatus.Error, $"Falha ao carregar {name} — {LocalAiStatusFormatter.HardwareLabel(ex.Hardware)}");
            throw;
        }
        catch (LocalModelUnavailableException)
        {
            await DisposeQuietlyAsync(runtime).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await DisposeQuietlyAsync(runtime).ConfigureAwait(false);
            SetStatus(new(LocalModelState.NotLoaded, "Carregamento cancelado; o modelo será carregado sob demanda.") { ModelName = name, RequestedHardware = key.Hardware });
            operation?.Complete(ApplicationOperationStatus.Cancelled, $"Carregamento de {name} cancelado");
            throw new LocalModelUnavailableException("Carregamento do modelo cancelado.");
        }
        catch (Exception ex)
        {
            await DisposeQuietlyAsync(runtime).ConfigureAwait(false);
            // Exception messages from native/model code can include paths or input; log only the type.
            diagnostics?.Record("model.load.failure", ex.GetType().Name);
            var message = ex switch
            {
                NotSupportedException => "Arquitetura não suportada por este runtime. Autocomplete básico ativo.",
                LocalModelLoadException { Stage: LocalModelLoadStage.Tokenizer } => "Falha ao carregar o tokenizer. Use tokenizer.json e tokenizer_config.json da mesma exportação do modelo.",
                _ => "Falha ao inicializar o modelo. Confira arquivos ONNX, memória e provider. Autocomplete básico ativo."
            };
            MarkFailed(key, new(ex is NotSupportedException ? LocalModelState.Unsupported : LocalModelState.Failed, message) { ModelName = name, RequestedHardware = key.Hardware });
            operation?.Complete(ApplicationOperationStatus.Error, $"Falha ao carregar {name}");
            throw new LocalModelUnavailableException(message, ex);
        }
    }

    private async Task FailGenerationAsync(ModelKey key, Exception exception, string? message = null)
    {
        // Exception messages from native/model code can include input or secrets; log only the type.
        diagnostics?.Record("autocomplete.ai.failure", exception.GetType().Name);
        MarkFailed(key, new(exception is NotSupportedException ? LocalModelState.Unsupported : LocalModelState.Failed,
            message ?? "Falha ao inicializar ou gerar. Confira modelo, tokenizer, memória e provider. Autocomplete básico ativo.")
        { ModelName = LoadedModel?.Name ?? ModelName(key.Path), RequestedHardware = key.Hardware });
        await UnloadCoreAsync().ConfigureAwait(false);
    }

    /// <summary>Releases the active or loading model before any other is loaded. Caller holds the gate.</summary>
    private async Task UnloadCoreAsync()
    {
        var loaded = _loaded;
        var loading = _loading;
        var cancellation = _loadCancellation;
        _loaded = null; _loading = null; _loadCancellation = null; _key = null;
        Volatile.Write(ref _loadedDefinition, null);
        if (loading is not null)
        {
            cancellation?.Cancel();
            try { loaded = await loading.ConfigureAwait(false); }
            catch (Exception) { /* Load failures and cancellation were already reported by the load itself. */ }
        }
        cancellation?.Dispose();
        if (loaded is null) return;
        await loaded.Runtime.DisposeAsync().ConfigureAwait(false);
        diagnostics?.Record("model.unload", loaded.Model.Id);
    }

    private static async Task DisposeQuietlyAsync(ILocalModelRuntime? runtime)
    {
        if (runtime is null) return;
        try { await runtime.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { /* The original failure is the one reported. */ }
    }

    private void MarkFailed(ModelKey key, LocalModelStatus status)
    {
        lock (_stateGate) { _failedKey = key; _retryAfter = _clock.GetUtcNow() + RetryDelay; }
        SetStatus(status);
    }

    private void ClearRetry()
    {
        lock (_stateGate) { _failedKey = null; _retryAfter = default; }
    }

    private void SetStatus(LocalModelStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private LocalModelStatus IdleStatus(AutocompleteSettings settings)
    {
        var path = settings.ResolveModelPath(LocalModelRole.Autocomplete, DefaultDirectory);
        return path.Length == 0
            ? new(LocalModelState.NotLoaded, "Nenhum modelo selecionado; autocomplete básico disponível.") { RequestedHardware = settings.Acceleration }
            : new(LocalModelState.NotLoaded, "Modelo ainda não carregado; será validado sob demanda.") { ModelName = ModelName(path), RequestedHardware = settings.Acceleration };
    }

    private static LocalModelStatus LoadedStatus(ActiveModel loaded, ModelKey key, TimeSpan elapsed)
    {
        var info = loaded.Info;
        var message = info is null ? "Modelo carregado."
            : $"Modelo carregado — {LocalAiStatusFormatter.HardwareLabel(info.Backend)} ({info.Provider})" + (info.UsedFallback ? "; aceleração preferida indisponível." : ".");
        return new(LocalModelState.Ready, message, info?.Provider)
        {
            ModelName = loaded.Model.Name, RequestedHardware = key.Hardware, Backend = info?.Backend, Device = info?.Device,
            LoadTime = info?.LoadTime ?? elapsed, ProcessMemoryBytes = info?.ProcessMemoryBytes, UsedFallback = info?.UsedFallback == true
        };
    }

    private static LocalModelStatus GenerationStatus(ActiveModel loaded, ModelKey key, ModelGenerationResult generated)
    {
        var info = loaded.Runtime.RuntimeInfo ?? loaded.Info;
        var fallback = generated.UsedCpuFallback || info?.UsedFallback == true;
        return new(LocalModelState.Ready, $"Pronto · {generated.Provider}{(fallback ? " (aceleração incompatível/indisponível → CPU)" : "")} · {generated.GeneratedTokens} token(s) em {generated.Elapsed.TotalMilliseconds:F0} ms",
            info?.Provider ?? generated.Provider)
        {
            ModelName = loaded.Model.Name, RequestedHardware = key.Hardware, Backend = info?.Backend, Device = info?.Device, LoadTime = info?.LoadTime,
            ProcessMemoryBytes = info?.ProcessMemoryBytes, FirstToken = generated.TimeToFirstToken, TokensPerSecond = TokensPerSecond(generated), UsedFallback = fallback
        };
    }

    private sealed record ModelKey(string Path, AiAccelerationMode Hardware, AiExecutionProvider Provider);
    private sealed record ActiveModel(LocalModelDefinition Model, ILocalModelRuntime Runtime, LocalModelRuntimeInfo? Info);

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
