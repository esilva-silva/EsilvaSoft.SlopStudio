using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IConsoleRuntime
{
    Task<ConsoleExecutionResult> ExecuteAsync(ConsoleRequest request,
        Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> confirmWrite, CancellationToken cancellationToken = default);
    ConsoleStatement GetStatement(string script, int caret);
}

public interface IConsoleHistoryRepository
{
    Task SaveConsoleHistoryAsync(ConsoleHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConsoleHistoryEntry>> GetConsoleHistoryAsync(int maximum = 100, CancellationToken cancellationToken = default);
}

/// <summary>Only validated proxy operations reach this boundary; no arbitrary commands or CLR objects.</summary>
public interface IConsoleDatabaseSession : IDisposable
{
    Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken);
}

public interface IConsoleDatabaseSessionFactory
{
    IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs);
}
