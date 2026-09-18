using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public interface IAiChatService
{
    Task<AiChatResponse?> AskAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}
