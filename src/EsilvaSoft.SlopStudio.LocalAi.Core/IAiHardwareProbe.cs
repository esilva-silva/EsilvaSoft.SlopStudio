using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

/// <summary>Backends the runtime of this build exposes; the UI never offers hardware that is not reported here.</summary>
public interface IAiHardwareProbe
{
    Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default);
}
