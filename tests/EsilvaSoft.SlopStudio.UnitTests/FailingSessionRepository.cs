using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

internal sealed class FailingSessionRepository : IWorkspaceSessionRepository
{
    public bool FailLoad { get; set; }
    public bool FailSave { get; set; } = true;
    public int SaveAttempts { get; private set; }
    public Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default) =>
        FailLoad ? Task.FromException<WorkspaceSession>(new InvalidDataException("snapshot ilegível")) : Task.FromResult(new WorkspaceSession());
    public Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default)
    {
        SaveAttempts++;
        return FailSave ? Task.FromException(new IOException("disco indisponível")) : Task.CompletedTask;
    }
}
