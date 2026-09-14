using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IAutocompleteDiagnostics
{
    void Record(string eventName, string? detail = null, TimeSpan? duration = null);
}

public sealed class AutocompleteService(AiAutocompleteProvider? ai = null, IAutocompleteDiagnostics? diagnostics = null,
    TimeProvider? timeProvider = null) : IAutocompleteService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, (AutocompleteResult Result, DateTimeOffset Expires)> _cache = [];
    private AutocompleteSettings _settings = new();
    private long _revision;
    public AutocompleteSettings Settings => Volatile.Read(ref _settings);
    public event EventHandler? SettingsChanged;
    public LocalModelStatus Status => ai?.Status ?? new(LocalModelState.NotInstalled, "IA não instalada; autocomplete básico disponível.");

    public AutocompleteResult? GetImmediateCompletion(AutocompleteRequest request) => Settings is { Enabled: true, UseDictionary: true }
        ? BasicAutocompleteProvider.GetCompletion(request.Bounded()) : null;

    public async Task ConfigureAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        lock (_gate)
        {
            _settings = settings;
            _revision++;
            _cache.Clear();
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        if (ai is not null) await ai.Models.SwitchModelAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        AutocompleteSettings settings; long revision;
        lock (_gate) { settings = _settings; revision = _revision; }
        if (!settings.Enabled) return null;
        request = request.Bounded();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new { request, revision }))));
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached) && cached.Expires > _clock.GetUtcNow()) return cached.Result;
        }
        AutocompleteResult? result = settings.UseDictionary ? BasicAutocompleteProvider.GetCompletion(request) : null;
        if (result is null && settings.Mode != AutocompleteMode.Basic && ai is not null
            && !CompletionPrivacy.ContainsSensitiveText(request.Prefix + request.Suffix + request.Context))
            result = await ai.GetCompletionAsync(request, settings, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result is null && settings.UseDictionary)
        {
            result = BasicAutocompleteProvider.GetCompletion(request);
            diagnostics?.Record("autocomplete.basic.used");
        }
        lock (_gate)
        {
            if (revision != _revision) return null;
            // Cache only successful AI inference: transient failures must be retried after cooldown.
            if (result is { IsAi: true })
            {
                if (_cache.Count >= 64) _cache.Remove(_cache.Keys.First());
                _cache[key] = (result, _clock.GetUtcNow().AddSeconds(30));
            }
        }
        return result;
    }

    public async Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default)
    {
        if (ai is null) return Status;
        var report = await RunModelTestAsync(cancellationToken).ConfigureAwait(false);
        return report.Succeeded ? ai.Status : new(LocalModelState.Failed, report.Message);
    }

    public async Task<LocalModelTestReport> RunModelTestAsync(CancellationToken cancellationToken = default)
    {
        if (ai is null) return new(false, "IA local não configurada nesta instalação; autocomplete básico disponível.", []);
        var settings = Settings;
        if (settings.Mode == AutocompleteMode.Basic)
            return new(false, "O modo Básico não usa IA local. Selecione Automático ou IA local para testar.", []) { Hardware = settings.Acceleration };
        return await ai.Models.TestModelAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Autocomplete over the shared local model service: prompt contract, privacy filter and output cleanup.</summary>
public sealed class AiAutocompleteProvider : IAsyncDisposable, IDisposable
{
    private readonly bool _ownsModels;

    public AiAutocompleteProvider(ILocalAiModelService models) => Models = models ?? throw new ArgumentNullException(nameof(models));

    /// <summary>Standalone composition that owns its model service.</summary>
    public AiAutocompleteProvider(ILocalModelCatalog catalog, Func<ILocalModelRuntime> runtimeFactory,
        IAutocompleteDiagnostics? diagnostics = null, TimeProvider? timeProvider = null)
        : this(new LocalAiModelService(catalog, runtimeFactory, diagnostics: diagnostics, timeProvider: timeProvider)) => _ownsModels = true;

    public ILocalAiModelService Models { get; }
    public LocalModelStatus Status => Models.Status;

    public async Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, AutocompleteSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        if (ContainsReservedOrSensitiveText(request.Prefix + request.Suffix + request.Context)) return null;
        LocalModelGeneration generation;
        try
        {
            generation = await Models.GenerateAsync(LocalModelRole.Autocomplete, settings, model => new ModelGenerationRequest(
                AutocompleteContextBuilder.ModelPrefix(request, LocalAiModelService.IsDeepSeek(model)), request.Suffix,
                settings.ContextTokens, settings.MaximumCompletionTokens, request.RequireComplete) { Temperature = model.Metadata?.Autocomplete.Temperature ?? 0 },
                AiRequestPriority.Background, cancellationToken).ConfigureAwait(false);
        }
        catch (LocalModelUnavailableException) { return null; }
        if (request.RequireComplete && !generation.Result.IsComplete) return null;
        return CleanGeneratedText(generation.Result.Text, request.Suffix) is { } text ? new(text, true, "IA local · Tab aceita · Esc descarta") : null;
    }

    public Task ResetAsync(CancellationToken cancellationToken = default) => Models.UnloadModelAsync(cancellationToken);

    internal static bool ContainsReservedOrSensitiveText(string source) => CompletionPrivacy.ContainsSensitiveText(source)
        || source.Contains("<|", StringComparison.Ordinal) || source.Contains("<｜", StringComparison.Ordinal);

    /// <summary>Returns null for output that must never reach the editor.</summary>
    internal static string? CleanGeneratedText(string text, string suffix)
    {
        // FIM exports can echo the existing suffix and then keep generating unrelated functions.
        // Preserve the actual document suffix instead of inserting a duplicate.
        if (suffix.Length >= 2 && text.IndexOf(suffix, StringComparison.Ordinal) is var suffixIndex && suffixIndex >= 0)
            text = text[..suffixIndex];
        return string.IsNullOrWhiteSpace(text) || text.Length > 8192 || text.Contains('\0') || text.Contains("```", StringComparison.Ordinal)
            || ContainsReservedOrSensitiveText(text) ? null : text;
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsModels) await Models.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
