using System.Diagnostics;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Native calls run on a worker. The model service serializes initialization, generation and disposal.</summary>
public sealed class OnnxLocalModelRuntime(IAutocompleteDiagnostics? diagnostics = null, IAiHardwareProbe? hardware = null) : ILocalModelRuntime
{
    // Zero lets ORT choose physical cores; a fixed two-thread cap starves large decoders.
    private const string SessionOverlay = "{\"model\":{\"decoder\":{\"session_options\":{\"intra_op_num_threads\":0,\"inter_op_num_threads\":1,\"log_severity_level\":4}}}}";
    private static readonly AiProviderCandidate CpuCandidate = new(AiAccelerationMode.Cpu, "CPU", "cpu", null);
    private Model? _model;
    private ITokenizer? _tokenizer;
    private ICompletionPromptBuilder _promptBuilder = new QwenFimPromptBuilder();
    private IReadOnlySet<int> _stops = new HashSet<int>();
    private AiProviderCandidate _provider = CpuCandidate;
    private AiExecutionPlan? _plan;
    private int _contextLength;
    private LocalModelDefinition? _definition;
    private AutocompleteSettings? _settings;
    private bool _fellBack;
    private LocalModelRuntimeInfo? _info;

    public LocalModelRuntimeInfo? RuntimeInfo => _info;

    public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
        InitializeAsync(model, settings, null, cancellationToken);

    public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, IProgress<string>? progress, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = ModelAdapters.For(model);
        Release();
        _definition = model;
        _settings = settings;
        _fellBack = false;
        _info = null;
        _promptBuilder = adapter.CreatePromptBuilder();
        OrtEnv.Instance().DisableTelemetryEvents();
        Utils.DisableTelemetryEvents();
        using (var file = File.OpenRead(Path.Combine(model.Path, "genai_config.json")))
        using (var json = JsonDocument.Parse(file))
            _contextLength = json.RootElement.GetProperty("model").GetProperty("context_length").GetInt32();
        var devices = hardware is null ? OnnxHardwareProbe.Detect() : await hardware.GetAvailableHardwareAsync(cancellationToken).ConfigureAwait(false);
        var plan = AiProviderSelector.Plan(settings, devices, model);
        _plan = plan;
        for (var index = 0; index < plan.Candidates.Count; index++)
        {
            var candidate = plan.Candidates[index];
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(candidate.Kind == AiAccelerationMode.Cpu ? $"Inicializando CPU — {model.Name}…"
                : $"Inicializando {LocalAiStatusFormatter.HardwareLabel(candidate.Kind)} ({candidate.Provider}) — {model.Name}…");
            var watch = Stopwatch.StartNew();
            try
            {
                Load(candidate, adapter, model);
                _provider = candidate;
                _fellBack = index > 0;
                using var process = Process.GetCurrentProcess();
                _info = new(candidate.Kind, candidate.Provider, candidate.Device, watch.Elapsed) { ProcessMemoryBytes = process.WorkingSet64, UsedFallback = _fellBack };
                diagnostics?.Record("model.ready", candidate.GenAiName, watch.Elapsed);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            catch (Exception ex) when (candidate.Kind != AiAccelerationMode.Cpu && !cancellationToken.IsCancellationRequested && ex is not LocalModelLoadException)
            {
                Release();
                if (plan.AllowFallback && index < plan.Candidates.Count - 1)
                {
                    diagnostics?.Record("provider.fallback", candidate.GenAiName + " → " + plan.Candidates[index + 1].GenAiName);
                    continue;
                }
                diagnostics?.Record("provider.load.failed", candidate.GenAiName);
                throw new AiProviderUnavailableException(candidate.Kind, Reason(candidate, ex), ex);
            }
            catch { Release(); throw; }
        }
    }, cancellationToken);

    public async Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default)
    {
        try { return await GenerateCoreAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (OnnxRuntimeGenAIException ex) when (_provider.Kind != AiAccelerationMode.Cpu && !cancellationToken.IsCancellationRequested)
        {
            var definition = _definition ?? throw new InvalidOperationException("Modelo não inicializado.");
            var settings = _settings ?? throw new InvalidOperationException("Modelo não inicializado.");
            var failed = _provider;
            if (_plan is not { AllowFallback: true } plan || !plan.Candidates.Any(candidate => candidate.Kind == AiAccelerationMode.Cpu))
            {
                // Explicit hardware: report the accelerated failure instead of silently continuing on CPU.
                diagnostics?.Record("provider.generation.failed", failed.GenAiName);
                throw new AiProviderUnavailableException(failed.Kind, Reason(failed, ex), ex);
            }
            diagnostics?.Record("provider.fallback", failed.GenAiName + " → cpu (geração)");
            // Keep the recovered CPU session loaded so the accelerated failure is not repeated on every keystroke.
            await InitializeAsync(definition, settings with { Acceleration = AiAccelerationMode.Cpu, ExecutionProvider = AiExecutionProvider.Auto }, cancellationToken).ConfigureAwait(false);
            _fellBack = true;
            _info = _info is null ? null : _info with { UsedFallback = true };
            return await GenerateCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Load(AiProviderCandidate candidate, IModelAdapter adapter, LocalModelDefinition model)
    {
        using (var config = new Config(model.Path))
        {
            config.ClearProviders();
            if (candidate.GenAiName != CpuCandidate.GenAiName) config.AppendProvider(candidate.GenAiName);
            config.Overlay(SessionOverlay);
            _model = new Model(config);
        }
        try
        {
            _tokenizer = adapter.CreateTokenizer(_model, model.Path);
            _ = _promptBuilder.Build("", "", 64, _tokenizer);
            _stops = adapter.GetStopTokens(_tokenizer);
        }
        catch (Exception ex)
        {
            diagnostics?.Record("tokenizer.load.failed", ex.GetType().Name);
            throw new LocalModelLoadException(LocalModelLoadStage.Tokenizer, "Tokenizer incompatível com este modelo.", ex);
        }
    }

    private Task<ModelGenerationResult> GenerateCoreAsync(ModelGenerationRequest request, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = _model ?? throw new InvalidOperationException("Modelo não inicializado.");
        var tokenizer = _tokenizer ?? throw new InvalidOperationException("Tokenizer não inicializado.");
        var context = Math.Min(request.ContextTokens, _contextLength - request.MaximumTokens);
        if (context < 4 || request.MaximumTokens < 1) throw new LocalModelContextException("Janela de contexto insuficiente.");
        if (request.RequireFullContext && tokenizer.Encode(request.Prefix).Count + tokenizer.Encode(request.Suffix).Count + 4 > context)
            throw new LocalModelContextException("O contexto completo excede a janela do modelo.");
        var input = _promptBuilder.Build(request.Prefix, request.Suffix, context, tokenizer).ToArray();
        var stops = _stops;
        using var parameters = new GeneratorParams(model);
        parameters.SetSearchOption("max_length", input.Length + request.MaximumTokens);
        parameters.SetSearchOption("do_sample", request.Temperature > 0);
        if (request.Temperature > 0) parameters.SetSearchOption("temperature", request.Temperature);
        using var generator = new Generator(model, parameters);
        var watch = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        using var registration = cancellationToken.Register(() =>
        {
            try { generator.SetRuntimeOption("terminate_session", "1"); }
            catch (Exception) { diagnostics?.Record("generation.cancel.failed"); }
        });
        try
        {
            generator.AppendTokens(input);
            for (var i = 0; i < request.MaximumTokens && !generator.IsDone(); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                generator.GenerateNextToken();
                firstToken ??= watch.Elapsed;
                if (stops.Contains(generator.GetSequence(0)[^1])) break;
                if (request.Suffix.Length >= 2 && tokenizer.Decode(generator.GetSequence(0)[input.Length..].ToArray())
                    .Contains(request.Suffix, StringComparison.Ordinal)) break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var output = generator.GetSequence(0)[input.Length..].ToArray().TakeWhile(id => !stops.Contains(id)).ToArray();
            // GenAI rejects decoding an empty sequence; an immediate stop token is a valid empty answer, not a model failure.
            return new ModelGenerationResult(output.Length == 0 ? "" : tokenizer.Decode(output), output.Length, watch.Elapsed, _provider.GenAiName,
                output.Length < request.MaximumTokens || stops.Contains(generator.GetSequence(0)[^1]), _fellBack) { TimeToFirstToken = firstToken };
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            // Dispose the callback before touching or disposing its generator; recover the reusable session.
            registration.Dispose();
            if (cancellationToken.IsCancellationRequested) generator.SetRuntimeOption("terminate_session", "0");
        }
    }, cancellationToken);

    /// <summary>Load and execution errors describe providers and devices, never editor text; keep one bounded line.</summary>
    private static string Reason(AiProviderCandidate candidate, Exception exception)
    {
        var line = exception is OnnxRuntimeGenAIException ? exception.Message.Split('\n', 2)[0].Trim() : exception.GetType().Name;
        // The native status message continues with the runtime's build path, which is noise for the user.
        if (line.IndexOf(" Status Message:", StringComparison.Ordinal) is > 0 and var status) line = line[..status].Trim();
        if (line.Length > 200) line = line[..200] + "…";
        return $"falha do provider {candidate.Provider}: {line}";
    }

    private void Release() { (_tokenizer as IDisposable)?.Dispose(); _tokenizer = null; _model?.Dispose(); _model = null; }
    public ValueTask DisposeAsync() { Release(); return ValueTask.CompletedTask; }
}
