using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IConsoleRuntime
{
    Task<ConsoleExecutionResult> ExecuteAsync(ConsoleRequest request,
        Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> confirmWrite, CancellationToken cancellationToken = default);
    ConsoleStatement GetStatement(string script, int caret);
}
