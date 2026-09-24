using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Normalized result shared by internal chat and protocol adapters.</summary>
public sealed class AgentToolInvocationResult
{
    private AgentToolInvocationResult(bool succeeded, string? errorCode, string? structuredContentJson,
        AgentAuditDecisionReason? auditReason = null)
    {
        Succeeded = succeeded;
        ErrorCode = errorCode;
        StructuredContentJson = structuredContentJson;
        AuditReason = auditReason;
    }

    public bool Succeeded { get; }
    public string? ErrorCode { get; }
    public string? StructuredContentJson { get; }
    internal AgentAuditDecisionReason? AuditReason { get; }

    internal static AgentToolInvocationResult Success(string structuredContentJson) =>
        new(true, null, structuredContentJson ?? throw new ArgumentNullException(nameof(structuredContentJson)));

    internal static AgentToolInvocationResult Failure(string errorCode) =>
        new(false, errorCode ?? throw new ArgumentNullException(nameof(errorCode)), null);

    internal static AgentToolInvocationResult Failure(string errorCode, AgentAuditDecisionReason auditReason) =>
        new(false, errorCode ?? throw new ArgumentNullException(nameof(errorCode)), null, auditReason);
}
