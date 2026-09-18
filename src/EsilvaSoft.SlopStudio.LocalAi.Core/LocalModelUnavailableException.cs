namespace EsilvaSoft.SlopStudio.LocalAi.Core;

/// <summary>Por que o modelo não pôde atender. Permite decidir sem interpretar a mensagem, que é texto de interface.</summary>
public enum LocalModelUnavailableReason : byte
{
    /// <summary>Motivo não classificado (validação, capacidade ausente, falha de carga).</summary>
    Unspecified,
    /// <summary>Nenhum modelo carregado e a política do pedido proíbe carregar.</summary>
    NotLoaded,
    /// <summary>Há modelo carregado, mas de outra chave (pasta, aceleração ou provider) e o pedido não pode trocá-lo.</summary>
    DifferentConfiguration
}

/// <summary>The local model cannot serve this request. Messages never contain editor text or raw native errors with prompts.</summary>
public class LocalModelUnavailableException : InvalidOperationException
{
    public LocalModelUnavailableException() { }
    public LocalModelUnavailableException(string message) : base(message) { }
    public LocalModelUnavailableException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Motivo tipado; <see cref="LocalModelUnavailableReason.Unspecified"/> quando não classificado. Nome distinto de
    /// <c>Reason</c> porque <c>AiProviderUnavailableException</c> já usa esse nome para o texto do provider.
    /// </summary>
    public LocalModelUnavailableReason UnavailableReason { get; init; }
}
