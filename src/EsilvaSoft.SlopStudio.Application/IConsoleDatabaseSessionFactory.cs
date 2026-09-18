using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IConsoleDatabaseSessionFactory
{
    IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs);
}
