namespace EsilvaSoft.SlopStudio.Application;

public interface IAutocompleteDiagnostics
{
    void Record(string eventName, string? detail = null, TimeSpan? duration = null);
}
