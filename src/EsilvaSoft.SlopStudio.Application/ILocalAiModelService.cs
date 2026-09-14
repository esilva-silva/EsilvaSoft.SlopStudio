using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public sealed record LocalModelGeneration(LocalModelDefinition Model, ModelGenerationResult Result);

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
    /// <exception cref="LocalModelUnavailableException">The model cannot serve the request; the message is safe to display.</exception>
    Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings, Func<LocalModelDefinition, ModelGenerationRequest> request,
        AiRequestPriority priority, CancellationToken cancellationToken = default);
    void CancelGeneration();
    /// <summary>Reloads the selected model and runs folder, tokenizer, session, provider and generation checks.</summary>
    Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>The local model cannot serve this request. Messages never contain editor text or raw native errors with prompts.</summary>
public class LocalModelUnavailableException : InvalidOperationException
{
    public LocalModelUnavailableException() { }
    public LocalModelUnavailableException(string message) : base(message) { }
    public LocalModelUnavailableException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>A background generation yielded the model to an explicit user action.</summary>
public sealed class LocalModelPreemptedException : LocalModelUnavailableException
{
    public LocalModelPreemptedException() : base("Geração em segundo plano substituída por uma ação explícita.") { }
    public LocalModelPreemptedException(string message) : base(message) { }
    public LocalModelPreemptedException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>The requested hardware cannot run the model. Outside Automatic mode there is no silent CPU fallback.</summary>
public sealed class AiProviderUnavailableException : LocalModelUnavailableException
{
    public AiProviderUnavailableException() { }
    public AiProviderUnavailableException(string message) : base(message) { }
    public AiProviderUnavailableException(string message, Exception? innerException) : base(message, innerException) { }
    public AiProviderUnavailableException(AiAccelerationMode hardware, string reason, Exception? innerException = null)
        : base(Format(hardware, reason), innerException) { Hardware = hardware; Reason = reason; }

    public AiAccelerationMode Hardware { get; }
    public string Reason { get; } = "";

    private static string Format(AiAccelerationMode hardware, string reason) => hardware == AiAccelerationMode.Auto
        ? $"Nenhum hardware disponível pode executar este modelo.\nMotivo: {reason}"
        : $"Não foi possível executar este modelo utilizando {LocalAiStatusFormatter.HardwareLabel(hardware)}.\nMotivo: {reason}\n"
          + (hardware == AiAccelerationMode.Cpu ? "Você pode selecionar: Automático." : "Você pode selecionar: Automático ou CPU.");
}

public enum LocalModelLoadStage { Tokenizer, Session }

public sealed class LocalModelLoadException : Exception
{
    public LocalModelLoadException() { }
    public LocalModelLoadException(string message) : base(message) { }
    public LocalModelLoadException(string message, Exception? innerException) : base(message, innerException) { }
    public LocalModelLoadException(LocalModelLoadStage stage, string message, Exception? innerException = null) : base(message, innerException) => Stage = stage;
    public LocalModelLoadStage Stage { get; } = LocalModelLoadStage.Session;
}

/// <summary>The request does not fit the model window. A request error, not a model failure: no unload or cooldown.</summary>
public sealed class LocalModelContextException : ArgumentException
{
    public LocalModelContextException() { }
    public LocalModelContextException(string message) : base(message) { }
    public LocalModelContextException(string message, Exception? innerException) : base(message, innerException) { }
}
