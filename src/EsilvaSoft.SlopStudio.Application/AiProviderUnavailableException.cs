using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>The requested hardware cannot run the model. Outside Automatic mode there is no silent CPU fallback.</summary>
public sealed class AiProviderUnavailableException : LocalModelUnavailableException
{
    // Todo construtor fixa o motivo tipado: o provider indisponível é uma linha própria da matriz de fallback e quem
    // a trata não pode depender de qual sobrecarga criou a exceção.
    public AiProviderUnavailableException() => UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    public AiProviderUnavailableException(string message) : base(message) => UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    public AiProviderUnavailableException(string message, Exception? innerException) : base(message, innerException) =>
        UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    public AiProviderUnavailableException(AiAccelerationMode hardware, string reason, Exception? innerException = null)
        : base(Format(hardware, reason), innerException)
    {
        Hardware = hardware;
        Reason = reason;
        UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    }

    public AiAccelerationMode Hardware { get; }
    public string Reason { get; } = "";

    private static string Format(AiAccelerationMode hardware, string reason) => hardware == AiAccelerationMode.Auto
        ? $"Nenhum hardware disponível pode executar este modelo.\nMotivo: {reason}"
        : $"Não foi possível executar este modelo utilizando {LocalAiStatusFormatter.HardwareLabel(hardware)}.\nMotivo: {reason}\n"
          + (hardware == AiAccelerationMode.Cpu ? "Você pode selecionar: Automático." : "Você pode selecionar: Automático ou CPU.");
}
