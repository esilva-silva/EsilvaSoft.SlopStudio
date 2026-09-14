using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IAutocompleteService
{
    AutocompleteSettings Settings { get; }
    LocalModelStatus Status { get; }
    event EventHandler? SettingsChanged;
    AutocompleteResult? GetImmediateCompletion(AutocompleteRequest request) => null;
    Task ConfigureAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default);
    Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default);
    Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default);

    /// <summary>Complete model check with steps and metrics; the default adapts <see cref="TestModelAsync"/>.</summary>
    async Task<LocalModelTestReport> RunModelTestAsync(CancellationToken cancellationToken = default)
    {
        var status = await TestModelAsync(cancellationToken).ConfigureAwait(false);
        return new(status.State == LocalModelState.Ready, status.Message, []);
    }
}

public interface ILocalModelCatalog
{
    string DefaultDirectory { get; }
    Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default);
    /// <summary>Each immediate subfolder of <paramref name="directory"/> is a candidate.</summary>
    Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(string directory, CancellationToken cancellationToken = default) => DiscoverAsync(cancellationToken);
    Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default);
}

public interface ILocalModelRuntime : IAsyncDisposable
{
    Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default);
    /// <summary>Progress receives short user-facing stages such as "Inicializando GPU…".</summary>
    Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, IProgress<string>? progress, CancellationToken cancellationToken = default) =>
        InitializeAsync(model, settings, cancellationToken);
    Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Effective backend after initialization, when the runtime can report it.</summary>
    LocalModelRuntimeInfo? RuntimeInfo => null;
}

/// <summary>Backends the runtime of this build exposes; the UI never offers hardware that is not reported here.</summary>
public interface IAiHardwareProbe
{
    Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default);
}

public interface ITokenizer
{
    IReadOnlyList<int> Encode(string text);
    string Decode(IEnumerable<int> tokens);
}

public interface ICompletionPromptBuilder
{
    IReadOnlyList<int> Build(string prefix, string suffix, int contextTokens, ITokenizer tokenizer);
}
