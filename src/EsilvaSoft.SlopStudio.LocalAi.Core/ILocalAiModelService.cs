using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

/// <summary>
/// Se um pedido de geração pode carregar (ou trocar) o modelo. <see cref="LoadedOnly"/> existe para o caminho
/// automático: digitar nunca pode iniciar carga, descarregar o modelo de outra funcionalidade nem trocar de
/// acelerador. O pedido explícito continua podendo carregar sob demanda.
/// </summary>
public enum AiModelLoadPolicy : byte
{
    /// <summary>Carrega o modelo desta chave sob demanda, descarregando o anterior quando a chave difere.</summary>
    LoadIfNeeded,
    /// <summary>Só atende se o modelo desta exata chave já estiver carregado; caso contrário recusa sem efeito algum.</summary>
    LoadedOnly
}

/// <summary>
/// Single owner of the local model shared by autocomplete, chat and future AI features. Callers never see the backend:
/// CPU, GPU and NPU are decided here and in the runtime.
/// </summary>
public interface ILocalAiModelService : IAsyncDisposable
{
    string DefaultDirectory { get; }
    LocalModelStatus Status { get; }
    LocalModelDefinition? LoadedModel { get; }
    event EventHandler? StatusChanged;
    Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default);
    Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default);
    /// <summary>Capabilities of the loaded model; <see cref="LocalModelCapabilities.None"/> when nothing is loaded.</summary>
    LocalModelCapabilities GetCapabilities();
    Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default);
    Task UnloadModelAsync(CancellationToken cancellationToken = default);
    /// <summary>Applies new preferences: cancels generations and releases the model only if its identity or use changed.</summary>
    Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default);
    /// <summary>
    /// Gera com o modelo do papel indicado. <c>load</c> = <see cref="AiModelLoadPolicy.LoadedOnly"/> recusa com
    /// <see cref="LocalModelUnavailableException"/> em vez de carregar, descarregar ou trocar o modelo.
    /// </summary>
    /// <exception cref="LocalModelUnavailableException">The model cannot serve the request; the message is safe to display.</exception>
    Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings, Func<LocalModelDefinition, ModelGenerationRequest> request,
        AiRequestPriority priority, AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default);
    void CancelGeneration();
    /// <summary>Reloads the selected model and runs folder, tokenizer, session, provider and generation checks.</summary>
    Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default);
}
