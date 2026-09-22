using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public interface IAiChatService
{
    /// <summary>Provides product-language explanations and safety messages; optional for headless callers.</summary>
    void SetLocalization(Func<string, string> localize) { }
    Task<AiChatResponse?> AskAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}
