namespace EsilvaSoft.SlopStudio.Application;

public sealed record CodeValidationResult(bool IsValid, string Message, int Offset = 0, int Length = 0);

public interface ICodeValidator
{
    Task<CodeValidationResult> ValidateAsync(string text, bool aggregation, CancellationToken cancellationToken = default);
}
