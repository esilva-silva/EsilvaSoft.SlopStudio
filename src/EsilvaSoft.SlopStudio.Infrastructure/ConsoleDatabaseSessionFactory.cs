using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class ConsoleDatabaseSessionFactory(MongoClientPool? clients = null) : IConsoleDatabaseSessionFactory
{
    public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) =>
        new ConsoleDatabaseSession(resolvedProfiles, documentLimit, timeoutMs, clients ?? MongoClientPool.Shared);
}
