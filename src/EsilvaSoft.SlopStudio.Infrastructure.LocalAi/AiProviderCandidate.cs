using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure.LocalAi;

public sealed record AiProviderCandidate(AiAccelerationMode Kind, string Provider, string GenAiName, string? Device);
