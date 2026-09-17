using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

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
