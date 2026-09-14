namespace EsilvaSoft.SlopStudio.Application;

public interface IResultPageExportService
{
    Task ExportAsync(string path, IReadOnlyList<string> documents, bool csv, Action<int, int>? progress = null, CancellationToken cancellationToken = default);
}
