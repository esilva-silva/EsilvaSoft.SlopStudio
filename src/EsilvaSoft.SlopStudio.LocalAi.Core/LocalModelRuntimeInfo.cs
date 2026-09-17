using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public sealed record LocalModelRuntimeInfo(AiAccelerationMode Backend, string Provider, string? Device, TimeSpan LoadTime)
{
    public long? ProcessMemoryBytes { get; init; }
    public bool UsedFallback { get; init; }
}
