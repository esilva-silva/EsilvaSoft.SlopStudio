namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Normalized result shared by internal chat and protocol adapters.</summary>
public sealed class AgentToolInvocationResult
{
    private AgentToolInvocationResult(bool succeeded, string? errorCode, string? structuredContentJson)
    {
        Succeeded = succeeded;
        ErrorCode = errorCode;
        StructuredContentJson = structuredContentJson;
    }

    public bool Succeeded { get; }
    public string? ErrorCode { get; }
    public string? StructuredContentJson { get; }

    internal static AgentToolInvocationResult Success(string structuredContentJson) =>
        new(true, null, structuredContentJson ?? throw new ArgumentNullException(nameof(structuredContentJson)));

    internal static AgentToolInvocationResult Failure(string errorCode) =>
        new(false, errorCode ?? throw new ArgumentNullException(nameof(errorCode)), null);
}
