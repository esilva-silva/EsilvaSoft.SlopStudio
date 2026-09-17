using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Only validated proxy operations reach this boundary; no arbitrary commands or CLR objects.</summary>
public interface IConsoleDatabaseSession : IDisposable
{
    Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken);
}
