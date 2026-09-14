using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IScriptHistoryRepository
{
    Task<IReadOnlyList<ScriptHistoryEntry>> GetRecentAsync(int maximum = 50, CancellationToken cancellationToken = default);

    Task SaveAsync(ScriptHistoryEntry entry, CancellationToken cancellationToken = default);
}
