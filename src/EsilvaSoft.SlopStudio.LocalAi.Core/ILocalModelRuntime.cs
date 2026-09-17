using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

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
