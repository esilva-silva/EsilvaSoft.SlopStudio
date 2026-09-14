using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IWorkspaceSessionRepository
{
    Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default);
    Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default);
}
