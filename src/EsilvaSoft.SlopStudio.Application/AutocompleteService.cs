using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

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
