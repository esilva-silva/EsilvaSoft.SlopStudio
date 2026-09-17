namespace EsilvaSoft.SlopStudio.LocalAi.Core;

/// <summary>The local model cannot serve this request. Messages never contain editor text or raw native errors with prompts.</summary>
public class LocalModelUnavailableException : InvalidOperationException
{
    public LocalModelUnavailableException() { }
    public LocalModelUnavailableException(string message) : base(message) { }
    public LocalModelUnavailableException(string message, Exception? innerException) : base(message, innerException) { }
}
