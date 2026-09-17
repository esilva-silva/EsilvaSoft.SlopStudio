using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

/// <summary>A backend reported by the runtime of this build on this machine.</summary>
public sealed record AiHardwareDevice(AiAccelerationMode Kind, string Provider, string Name, bool IsAvailable)
{
    public long? MemoryBytes { get; init; }
    public string? Reason { get; init; }
}
