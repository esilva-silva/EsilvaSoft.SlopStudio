namespace EsilvaSoft.SlopStudio.Application;

public interface ICodeFormatter
{
    Task<string> FormatAsync(string text, CancellationToken cancellationToken = default);
}
