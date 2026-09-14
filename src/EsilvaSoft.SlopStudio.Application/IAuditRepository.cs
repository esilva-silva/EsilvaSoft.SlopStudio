using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IAuditRepository
{
    Task<IReadOnlyList<AuditEntry>> GetRecentAuditAsync(int maximum = 100, CancellationToken cancellationToken = default);

    Task SaveAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
