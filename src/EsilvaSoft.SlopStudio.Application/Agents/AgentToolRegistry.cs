using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using System.Buffers;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Default-deny internal executable tool registry.</summary>
public sealed partial class AgentToolRegistry : IAgentToolRegistry
{
    public const string ListConnectionsToolName = "list_connections";
    public const string ListDatabasesToolName = "list_databases";
    public const string ListCollectionsToolName = "list_collections";
    public const string GetCollectionSchemaToolName = "get_collection_schema";
    public const string MongoFindToolName = "mongo_find";
    public const string MongoCountToolName = "mongo_count";
    public const string SampleDocumentsToolName = "sample_documents";
    public const string MongoFindOneToolName = "mongo_find_one";
    public const string GetDocumentToolName = "get_document";
    public const string MongoDistinctToolName = "mongo_distinct";
    public const string GetIndexesToolName = "get_indexes";
    public const string MongoExplainToolName = "mongo_explain";
    private const int MaximumInputBytes = 64 * 1024;
    private const int MaximumConnections = 200;
    private const string PermissionDenied = "PermissionDenied";
    private const string InvalidArguments = "InvalidArguments";
    private const string UnknownTool = "UnknownTool";
    private const string ResultTooLarge = "ResultTooLarge";
    private const string DeadlineExceeded = "DeadlineExceeded";
    private const int MaximumOutputBytes = 256 * 1024;
    private static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumExecutionTimeout = TimeSpan.FromSeconds(30);

    private const string ListConnectionsInputSchema = """
        {"type":"object","properties":{},"additionalProperties":false}
        """;

    private const string ListConnectionsOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["connections","truncated"],"properties":{"connections":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","name","readOnly"],"properties":{"id":{"type":"string","format":"uuid"},"name":{"type":"string"},"readOnly":{"type":"boolean"}}}},"truncated":{"type":"boolean"}}}
        """;

    private const string ListDatabasesInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId"],"properties":{"connectionId":{"type":"string","format":"uuid"},"skip":{"type":"integer","minimum":0,"maximum":10000}}}
        """;
    private const string ListCollectionsInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"skip":{"type":"integer","minimum":0,"maximum":10000}}}
        """;
    private const string NamesOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["names","truncated"],"properties":{"names":{"type":"array","maxItems":200,"items":{"type":"string"}},"truncated":{"type":"boolean"}}}
        """;
    private const string GetCollectionSchemaInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255},"sampleSize":{"type":"integer","minimum":1,"maximum":100,"default":20}}}
        """;
    private const string GetCollectionSchemaOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["fields","sampleSize","observedAt","source","isPartial"],"properties":{"fields":{"type":"array","maxItems":200,"items":{"type":"object","additionalProperties":false,"required":["path","bsonTypes","observedCount"],"properties":{"path":{"type":"string"},"bsonTypes":{"type":"array","items":{"type":"string"}},"observedCount":{"type":"integer","minimum":1}}}},"sampleSize":{"type":"integer","minimum":0,"maximum":100},"observedAt":{"type":"string","format":"date-time"},"source":{"type":"string","const":"sample"},"isPartial":{"type":"boolean"}}}
        """;
    private const string MongoFindInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255},"filterEjson":{"type":"string","maxLength":65536,"default":"{}"},"projectionEjson":{"type":"string","maxLength":65536},"sortEjson":{"type":"string","maxLength":65536},"limit":{"type":"integer","minimum":1,"maximum":100,"default":20},"skip":{"type":"integer","minimum":0,"maximum":10000,"default":0},"maxTimeMs":{"type":"integer","minimum":1,"maximum":30000,"default":5000}}}
        """;
    private const string MongoFindOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["documentsEjson","returnedCount","hasMore","truncated"],"properties":{"documentsEjson":{"type":"array","maxItems":100,"items":{"type":"string"}},"returnedCount":{"type":"integer","minimum":0,"maximum":100},"hasMore":{"type":"boolean"},"truncated":{"type":"boolean"},"truncationReason":{"type":"string","const":"OutputLimit"}}}
        """;
    private const string MongoCountInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255},"filterEjson":{"type":"string","maxLength":65536,"default":"{}"},"maxTimeMs":{"type":"integer","minimum":1,"maximum":30000,"default":5000}}}
        """;
    private const string MongoCountOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["countEjson","estimated"],"properties":{"countEjson":{"type":"string"},"estimated":{"type":"boolean","const":false}}}
        """;
    private const string SampleDocumentsInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255},"limit":{"type":"integer","minimum":1,"maximum":20,"default":5},"projectionEjson":{"type":"string","maxLength":65536}}}
        """;
    private const string SampleDocumentsOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["documentsEjson","returnedCount","hasMore","truncated"],"properties":{"documentsEjson":{"type":"array","maxItems":20,"items":{"type":"string"}},"returnedCount":{"type":"integer","minimum":0,"maximum":20},"hasMore":{"type":"boolean"},"truncated":{"type":"boolean"},"truncationReason":{"type":"string","const":"OutputLimit"}}}
        """;
    private const string MongoFindOneInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255},"filterEjson":{"type":"string","maxLength":65536,"default":"{}"},"projectionEjson":{"type":"string","maxLength":65536},"sortEjson":{"type":"string","maxLength":65536},"maxTimeMs":{"type":"integer","minimum":1,"maximum":30000,"default":5000}}}
        """;
    private const string MongoFindOneOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["documentEjson"],"properties":{"documentEjson":{"type":["string","null"]}}}
        """;
    private const string GetDocumentInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection","idEjson"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255},"idEjson":{"type":"string","maxLength":65536}}}
        """;
    private const string MongoDistinctInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection","field"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255},"field":{"type":"string","minLength":1,"maxLength":1024},"filterEjson":{"type":"string","maxLength":65536,"default":"{}"},"maximumValues":{"type":"integer","minimum":1,"maximum":100,"default":20},"maxTimeMs":{"type":"integer","minimum":1,"maximum":30000,"default":5000}}}
        """;
    private const string MongoDistinctOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["valuesEjson","truncated"],"properties":{"valuesEjson":{"type":"array","maxItems":100,"items":{"type":"string"}},"truncated":{"type":"boolean"},"truncationReason":{"type":"string","enum":["ValueLimit","OutputLimit"]}}}
        """;
    private const string GetIndexesInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255}}}
        """;
    private const string GetIndexesOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["indexes","truncated"],"properties":{"indexes":{"type":"array","maxItems":200,"items":{"type":"object","additionalProperties":false,"required":["name","keyFields","unique","sparse","hidden"],"properties":{"name":{"type":"string"},"keyFields":{"type":"array","items":{"type":"string"}},"unique":{"type":"boolean"},"sparse":{"type":"boolean"},"hidden":{"type":"boolean"}}}},"truncated":{"type":"boolean"},"truncationReason":{"type":"string","const":"OutputLimit"}}}
        """;
    private const string MongoExplainInputSchema = MongoFindInputSchema;
    private const string MongoExplainOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["planEjson","verbosity"],"properties":{"planEjson":{"type":"string"},"verbosity":{"type":"string","const":"queryPlanner"}}}
        """;

    private static readonly ReadOnlyCollection<AgentToolDescriptor> Descriptors = Array.AsReadOnly(
        [new AgentToolDescriptor(ListConnectionsToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(ListDatabasesToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(ListCollectionsToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(GetCollectionSchemaToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ReadSchema, AgentPermission.ExecuteReadQueries]),
         new AgentToolDescriptor(MongoFindToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments]),
         new AgentToolDescriptor(MongoCountToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments]),
         new AgentToolDescriptor(SampleDocumentsToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments]),
         new AgentToolDescriptor(MongoFindOneToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments]),
         new AgentToolDescriptor(GetDocumentToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments]),
         new AgentToolDescriptor(MongoDistinctToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments]),
         new AgentToolDescriptor(GetIndexesToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(MongoExplainToolName, 1, AgentToolRisk.ReadOnly,
             [AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments])]);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionProfileRepository _profiles;
    private readonly IAgentAuthorizationPolicyProvider _policies;
    private readonly IAgentPermissionEvaluator _permissions;
    private readonly IAgentAuditRepository _audit;
    private readonly IMongoMetadataSource? _metadata;
    private readonly IAgentSchemaSamplingConsentProvider? _schemaSamplingConsent;
    private readonly IAgentMongoFindSource? _find;
    private readonly IAgentMongoCountSource? _count;
    private readonly IAgentMongoDistinctSource? _distinct;
    private readonly IAgentMongoIndexSource? _indexes;
    private readonly IAgentMongoExplainSource? _explain;
    private readonly AgentToolInvocationQuota _quota = new();
    private readonly AsyncLocal<AgentToolInvocationQuota.Lease?> _activeQuotaLease = new();
    private readonly TimeSpan _executionTimeout;

    public AgentToolRegistry(
        IConnectionProfileRepository profiles,
        IAgentAuthorizationPolicyProvider policies,
        IAgentPermissionEvaluator permissions,
        IAgentAuditRepository audit,
        TimeSpan? executionTimeout = null,
        IMongoMetadataSource? metadata = null,
        IAgentSchemaSamplingConsentProvider? schemaSamplingConsent = null,
        IAgentMongoFindSource? find = null,
        IAgentMongoCountSource? count = null,
        IAgentMongoDistinctSource? distinct = null,
        IAgentMongoIndexSource? indexes = null,
        IAgentMongoExplainSource? explain = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _metadata = metadata;
        _schemaSamplingConsent = schemaSamplingConsent;
        _find = find;
        _count = count;
        _distinct = distinct;
        _indexes = indexes;
        _explain = explain;
        var requestedTimeout = executionTimeout ?? DefaultExecutionTimeout;
        if (requestedTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(executionTimeout));
        _executionTimeout = requestedTimeout > MaximumExecutionTimeout ? MaximumExecutionTimeout : requestedTimeout;
    }

    public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => Descriptors;

    public AgentToolDescriptor? FindDescriptor(string? name) =>
        Descriptors.FirstOrDefault(descriptor => string.Equals(name, descriptor.Name, StringComparison.Ordinal));

    public string? GetInputSchemaJson(string? name) =>
        name switch
        {
            ListConnectionsToolName => ListConnectionsInputSchema,
            ListDatabasesToolName => ListDatabasesInputSchema,
            ListCollectionsToolName => ListCollectionsInputSchema,
            GetCollectionSchemaToolName => GetCollectionSchemaInputSchema,
            MongoFindToolName => MongoFindInputSchema,
            MongoCountToolName => MongoCountInputSchema,
            SampleDocumentsToolName => SampleDocumentsInputSchema,
            MongoFindOneToolName => MongoFindOneInputSchema,
            GetDocumentToolName => GetDocumentInputSchema,
            MongoDistinctToolName => MongoDistinctInputSchema,
            GetIndexesToolName => GetIndexesInputSchema,
            MongoExplainToolName => MongoExplainInputSchema,
            _ => null
        };

    public string? GetOutputSchemaJson(string? name) =>
        name switch
        {
            ListConnectionsToolName => ListConnectionsOutputSchema,
            ListDatabasesToolName or ListCollectionsToolName => NamesOutputSchema,
            GetCollectionSchemaToolName => GetCollectionSchemaOutputSchema,
            MongoFindToolName => MongoFindOutputSchema,
            MongoCountToolName => MongoCountOutputSchema,
            SampleDocumentsToolName => SampleDocumentsOutputSchema,
            MongoFindOneToolName => MongoFindOneOutputSchema,
            GetDocumentToolName => MongoFindOneOutputSchema,
            MongoDistinctToolName => MongoDistinctOutputSchema,
            GetIndexesToolName => GetIndexesOutputSchema,
            MongoExplainToolName => MongoExplainOutputSchema,
            _ => null
        };

    public async Task<AgentToolInvocationResult> InvokeAsync(
        AgentPrincipal? principal,
        AgentInvocationContext? invocationContext,
        AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope,
        string? name,
        string? argumentsJson,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_executionTimeout);
        // Only a trusted principal and a complete invocation can identify an auditable operation.
        // Keep the registry private until an authenticated ingress constructs both objects.
        var auditable = principal is not null && IsCompleteInvocationContext(invocationContext) &&
            destination is not null && IsValidDestination(destination, invocationContext!) &&
            FindDescriptor(name) is not null;
        AgentAuditEvent? intent = null;
        var intentWritten = false;
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (auditable)
            {
                intent = CreateAuditIntent(principal!, invocationContext!, destination!, name!, argumentsJson);
                try
                {
                    await AwaitWithCancellationAsync(_audit.AppendAsync(intent, deadline.Token), deadline.Token)
                        .ConfigureAwait(false);
                    intentWritten = true;
                }
                catch (OperationCanceledException) when (deadline.Token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return AgentToolInvocationResult.Failure(PermissionDenied);
                }
            }

            // The intent is durable before admission. A rejected call is still counted for its
            // turn and receives a correlated, typed audit outcome without touching a source.
            using var quotaLease = auditable
                ? _quota.TryEnter(invocationContext!.SessionId!.Value, invocationContext.TurnId!.Value,
                    intent!.ConnectionId)
                : null;
            var result = auditable && quotaLease is null
                ? AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.LimitExceeded)
                : await InvokeWithQuotaLeaseAsync(quotaLease, principal, invocationContext, destination,
                    outputDataScope, name, argumentsJson, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (intentWritten && !await AppendOutcomeAsync(intent!, result).ConfigureAwait(false))
                return AgentToolInvocationResult.Failure(PermissionDenied);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            if (intentWritten && !await AppendOutcomeAsync(intent!,
                    AgentToolInvocationResult.Failure(DeadlineExceeded)).ConfigureAwait(false))
                return AgentToolInvocationResult.Failure(PermissionDenied);
            return AgentToolInvocationResult.Failure(DeadlineExceeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (intentWritten)
                await AppendOutcomeAsync(intent!, null).ConfigureAwait(false);
            throw;
        }
        catch
        {
            if (intentWritten)
                await AppendOutcomeAsync(intent!, AgentToolInvocationResult.Failure("ExecutionFailed"))
                    .ConfigureAwait(false);
            return AgentToolInvocationResult.Failure(PermissionDenied);
        }
    }

    private async Task<AgentToolInvocationResult> InvokeWithQuotaLeaseAsync(
        AgentToolInvocationQuota.Lease? lease, AgentPrincipal? principal,
        AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
        CancellationToken cancellationToken)
    {
        var previous = _activeQuotaLease.Value;
        _activeQuotaLease.Value = lease;
        try
        {
            return await InvokeWithinDeadlineAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _activeQuotaLease.Value = previous;
        }
    }

    private static AgentAuditEvent CreateAuditIntent(
        AgentPrincipal principal, AgentInvocationContext context, AgentOutputDestination destination,
        string name, string? argumentsJson)
    {
        var startedAt = DateTimeOffset.UtcNow;
        Guid connectionId;
        string? database;
        bool parsed;
        if (name == MongoFindToolName)
            parsed = TryParseFindArguments(argumentsJson, out connectionId, out database, out _, out _);
        else if (name == MongoCountToolName)
            parsed = TryParseCountArguments(argumentsJson, out connectionId, out database, out _, out _);
        else if (name == SampleDocumentsToolName)
            parsed = TryParseSampleDocumentsArguments(argumentsJson, out connectionId, out database, out _, out _);
        else if (name == MongoFindOneToolName)
            parsed = TryParseFindOneArguments(argumentsJson, out connectionId, out database, out _, out _);
        else if (name == GetDocumentToolName)
            parsed = TryParseGetDocumentArguments(argumentsJson, out connectionId, out database, out _, out _);
        else if (name == MongoDistinctToolName)
            parsed = TryParseDistinctArguments(argumentsJson, out connectionId, out database, out _, out _);
        else if (name == GetIndexesToolName)
            parsed = TryParseGetIndexesArguments(argumentsJson, out connectionId, out database, out _);
        else if (name == MongoExplainToolName)
            parsed = TryParseFindArguments(argumentsJson, out connectionId, out database, out _, out _);
        else if (name == GetCollectionSchemaToolName)
            parsed = TryParseSchemaArguments(argumentsJson, out connectionId, out database, out _, out _);
        else
            parsed = TryParseMetadataArguments(name, argumentsJson, out connectionId, out database, out _);
        var permission = name switch
        {
            GetCollectionSchemaToolName => AgentPermission.ReadSchema,
            MongoFindToolName => AgentPermission.ReadDocuments,
            MongoCountToolName => AgentPermission.ReadDocuments,
            SampleDocumentsToolName => AgentPermission.ReadDocuments,
            MongoFindOneToolName => AgentPermission.ReadDocuments,
            GetDocumentToolName => AgentPermission.ReadDocuments,
            MongoDistinctToolName => AgentPermission.ReadDocuments,
            MongoExplainToolName => AgentPermission.ReadDiagnostics,
            _ => AgentPermission.ReadMetadata
        };
        return new AgentAuditEvent(Guid.NewGuid(), AgentAuditEvent.CurrentSchemaVersion, startedAt,
            principal.Id, Guid.NewGuid(), context.SessionId, context.TurnId,
            destination.Kind == AgentOutputDestinationKind.Local ? AgentAuditChannel.Internal : AgentAuditChannel.ProviderExternal,
            destination.ProviderId,
            name, 1, AgentToolRisk.ReadOnly, permission,
            AgentAuditDecision.Requested, AgentAuditOutcome.Intent, principal.PolicyRevision,
            parsed && connectionId != Guid.Empty ? connectionId : null, 0, 0, 0)
        {
            NamespaceKind = database is null ? AgentAuditNamespaceKind.None : AgentAuditNamespaceKind.Pseudonym,
            NamespacePseudonym = database is null ? null : Guid.NewGuid().ToString("N"),
            DecisionReason = AgentAuditDecisionReason.NotEvaluated,
            ApprovalState = AgentAuditApprovalState.NotRequired,
            StartedAtUtc = startedAt
        }.Validate();
    }

    private async Task<bool> AppendOutcomeAsync(AgentAuditEvent intent, AgentToolInvocationResult? result)
    {
        var succeeded = result?.Succeeded == true;
        var cancelled = result is null || result.ErrorCode == DeadlineExceeded;
        var denied = result?.ErrorCode is PermissionDenied or InvalidArguments or UnknownTool;
        var reason = succeeded ? AgentAuditDecisionReason.PolicyAllowed : cancelled ? AgentAuditDecisionReason.Cancelled :
            result?.AuditReason is { } auditReason ? auditReason :
            result?.ErrorCode == InvalidArguments ? AgentAuditDecisionReason.ValidationRejected :
            result?.ErrorCode == ResultTooLarge ? AgentAuditDecisionReason.LimitExceeded :
            denied ? AgentAuditDecisionReason.PermissionMissing : AgentAuditDecisionReason.ExecutionFailed;
        var outcome = succeeded ? AgentAuditOutcome.Succeeded : cancelled ? AgentAuditOutcome.Cancelled :
            reason == AgentAuditDecisionReason.ExecutionFailed ? AgentAuditOutcome.Failed :
            denied ? AgentAuditOutcome.Denied : AgentAuditOutcome.Failed;
        try
        {
            var completedAt = DateTimeOffset.UtcNow;
            var elapsed = completedAt - intent.StartedAtUtc;
            // An unrepresentable duration leaves a visible unresolved intent and suppresses output.
            if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMilliseconds(AgentAuditEvent.MaximumDurationMilliseconds))
                return false;
            var duration = (elapsed.Ticks + TimeSpan.TicksPerMillisecond - 1) /
                TimeSpan.TicksPerMillisecond;
            var itemCount = 0;
            var outputBytes = 0;
            if (succeeded && result?.StructuredContentJson is { } json)
            {
                outputBytes = Encoding.UTF8.GetByteCount(json);
                using var document = JsonDocument.Parse(json);
                itemCount = intent.ToolName is MongoCountToolName or MongoExplainToolName ? 1 :
                    intent.ToolName is MongoFindOneToolName or GetDocumentToolName
                        ? document.RootElement.GetProperty("documentEjson").ValueKind == JsonValueKind.Null ? 0 : 1
                        :
                    document.RootElement.GetProperty(intent.ToolName switch
                    {
                        ListConnectionsToolName => "connections",
                        GetCollectionSchemaToolName => "fields",
                        MongoFindToolName => "documentsEjson",
                        SampleDocumentsToolName => "documentsEjson",
                        MongoDistinctToolName => "valuesEjson",
                        GetIndexesToolName => "indexes",
                        _ => "names"
                    }).GetArrayLength();
            }
            var terminal = intent with
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = completedAt,
                Decision = succeeded ? AgentAuditDecision.Allowed : AgentAuditDecision.Denied,
                Outcome = outcome,
                DurationMilliseconds = duration,
                ItemCount = itemCount,
                OutputBytes = outputBytes,
                DecisionReason = reason,
                CompletedAtUtc = completedAt
            };
            using var appendDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await AwaitWithCancellationAsync(_audit.AppendAsync(terminal, appendDeadline.Token), appendDeadline.Token)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<AgentToolInvocationResult> InvokeWithinDeadlineAsync(
        AgentPrincipal? principal,
        AgentInvocationContext? invocationContext,
        AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope,
        string? name,
        string? argumentsJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FindDescriptor(name) is null) return AgentToolInvocationResult.Failure(UnknownTool);
        if (name is ListDatabasesToolName or ListCollectionsToolName)
            return await InvokeMetadataAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == GetCollectionSchemaToolName)
            return await InvokeSchemaAsync(principal, invocationContext, destination, outputDataScope,
                argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == MongoFindToolName)
            return await InvokeFindAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == SampleDocumentsToolName)
            return await InvokeFindAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == MongoFindOneToolName)
            return await InvokeFindAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == GetDocumentToolName)
            return await InvokeFindAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == MongoDistinctToolName)
            return await InvokeDistinctAsync(principal, invocationContext, destination, outputDataScope,
                argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == GetIndexesToolName)
            return await InvokeIndexesAsync(principal, invocationContext, destination, outputDataScope,
                argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == MongoExplainToolName)
            return await InvokeExplainAsync(principal, invocationContext, destination, outputDataScope,
                argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == MongoCountToolName)
            return await InvokeCountAsync(principal, invocationContext, destination, outputDataScope,
                argumentsJson, cancellationToken).ConfigureAwait(false);
        if (!IsClosedEmptyObject(argumentsJson)) return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null) return AgentToolInvocationResult.Failure(PermissionDenied);
        if (!IsCompleteInvocationContext(invocationContext) || destination is null ||
            outputDataScope != AgentOutputDataScope.Metadata || !IsValidDestination(destination, invocationContext!))
            return AgentToolInvocationResult.Failure(PermissionDenied);

        // A valid current policy is checked before even enumerating local profile names.
        var initialLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (initialLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, initialLoad.DenialReason);
        var initialPolicy = initialLoad.Policy;

        IReadOnlyList<ConnectionProfile> loaded;
        try
        {
            loaded = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        // Freeze the internal profiles before any per-profile policy reads. Only allowlisted fields are projected.
        ConnectionProfile?[] snapshot;
        try
        {
            if (loaded is null) return AgentToolInvocationResult.Failure(PermissionDenied);
            snapshot = loaded.Cast<ConnectionProfile?>().ToArray();
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied);
        }

        var authorized = new List<ConnectionSummary>(MaximumConnections + 1);
        var authorizedSnapshots = new Dictionary<Guid, AuthorizedConnectionSnapshot>(MaximumConnections);
        var outputBytes = Utf8ByteCount("{\"connections\":[") + Utf8ByteCount("],\"truncated\":false}");
        var truncated = false;
        foreach (var profile in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (profile is null || profile.Id == Guid.Empty || profile.SourceGenerationId is not Guid generationId || generationId == Guid.Empty ||
                !IsValidProfileName(profile.Name))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);

            AgentPermissionDecision decision;
            try
            {
                decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                    new AgentPermissionRequest(
                        principal,
                        AgentPermission.ReadMetadata,
                        AgentToolRisk.ReadOnly,
                        AgentNamespaceScope.ForConnection(profile.Id),
                        initialPolicy.Revision,
                        profile.IsReadOnly,
                        invocationContext,
                        generationId,
                        destination,
                        outputDataScope),
                    cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
            }

            if (decision.PolicyRevision != initialPolicy.Revision)
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
            if (!decision.IsAllowed)
            {
                if (decision.Reason == AgentPermissionDenialReason.MissingGrant) continue;
                return AgentToolInvocationResult.Failure(PermissionDenied, MapDenialReason(decision.Reason));
            }

            if (authorized.Count == MaximumConnections)
            {
                truncated = true;
                break;
            }

            // A user-defined name is arbitrary text and can contain credentials. External recipients receive
            // only an ID-derived alias; pattern matching cannot prove that a free-text name contains no secret.
            var outputName = destination.Kind == AgentOutputDestinationKind.ProviderExternal
                ? $"Conexão {profile.Id:D}"
                : profile.Name;
            var summary = new ConnectionSummary(profile.Id, outputName, profile.IsReadOnly);
            if (Utf8ByteCount(outputName) > MaximumOutputBytes)
            {
                if (authorized.Count == 0) return AgentToolInvocationResult.Failure(ResultTooLarge);
                truncated = true;
                break;
            }

            var itemJson = JsonSerializer.SerializeToUtf8Bytes(summary, SerializerOptions);
            var itemBytes = itemJson.Length + (authorized.Count == 0 ? 0 : 1);
            if (outputBytes + itemBytes > MaximumOutputBytes)
            {
                if (authorized.Count == 0) return AgentToolInvocationResult.Failure(ResultTooLarge);
                truncated = true;
                break;
            }

            if (!authorizedSnapshots.TryAdd(profile.Id, new AuthorizedConnectionSnapshot(generationId, profile.Name, summary)))
                return AgentToolInvocationResult.Failure(PermissionDenied);
            authorized.Add(summary);
            outputBytes += itemBytes;
        }

        // Revalidate the projected values and origin after all asynchronous permission reads. This does not
        // replace the eventual transport's authorization check at the point it releases output.
        if (await RevalidateProfilesAsync(authorizedSnapshots, cancellationToken).ConfigureAwait(false) is { } revalidationFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, revalidationFailure);

        // Keep policy validation last, including revocation during the final profile read.
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != initialPolicy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var output = new ListConnectionsResponse(authorized, truncated);
        var json = JsonSerializer.Serialize(output, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumOutputBytes)
            return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json);
    }

    private async Task<AgentToolInvocationResult> InvokeMetadataAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string name, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseMetadataArguments(name, argumentsJson, out var connectionId, out var database, out var skip))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentOutputDataScope.Metadata || _metadata is null)
            return AgentToolInvocationResult.Failure(PermissionDenied);

        var initialLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (initialLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, initialLoad.DenialReason);
        var policy = initialLoad.Policy;

        ConnectionProfile profile;
        try
        {
            var profiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (profiles is null) return AgentToolInvocationResult.Failure(PermissionDenied);
            var matching = profiles.Where(item => item?.Id == connectionId).Take(2).ToArray();
            if (matching.Length != 1 || matching[0] is not { SourceGenerationId: { } generation } ||
                generation == Guid.Empty || !IsValidProfileName(matching[0].Name))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            profile = matching[0];
            if (HasDynamicMongoTarget(profile))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        var generationId = profile.SourceGenerationId!.Value;
        // A scoped grant permits enumerating only to discover its own names. No result is released by this preflight.
        var hasEligibleGrant = policy.Grants.Any(grant => grant.PrincipalId == principal.Id &&
            grant.Scope.ConnectionId == connectionId && grant.SourceGenerationId == generationId &&
            grant.Permission == AgentPermission.ReadMetadata && grant.InvocationScope.Covers(context!) &&
            grant.Destination == destination && grant.OutputDataScope == outputScope &&
            (database is null || grant.Scope.DatabaseName is null ||
             string.Equals(grant.Scope.DatabaseName, database, StringComparison.Ordinal)));
        if (!hasEligibleGrant)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);

        IReadOnlyList<string> names;
        bool overflow;
        try
        {
            if (name == ListDatabasesToolName)
            {
                var result = await AwaitWithCancellationAsync(
                    _metadata.ListDatabaseNamesBoundedAsync(profile, 10_000, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                names = result?.Items!;
                overflow = result?.Overflow ?? true;
            }
            else
            {
                var result = await AwaitWithCancellationAsync(
                    _metadata.ListCollectionNamesBoundedAsync(profile, database!, 10_000, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                if (result is null || result.Overflow || result.Items is null || result.Items.Count > 10_000)
                    return AgentToolInvocationResult.Failure(ResultTooLarge, AgentAuditDecisionReason.LimitExceeded);
                names = result.Items.Select(item => item?.Name!).ToArray();
                overflow = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }
        if (overflow || names is null || names.Count > 10_000)
            return AgentToolInvocationResult.Failure(ResultTooLarge, AgentAuditDecisionReason.LimitExceeded);

        var allowed = new List<string>(names.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafeMetadataName(item) || !seen.Add(item))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            var scope = name == ListDatabasesToolName
                ? AgentNamespaceScope.ForDatabase(connectionId, item)
                : AgentNamespaceScope.ForCollection(connectionId, database!, item);
            AgentPermissionDecision decision;
            try
            {
                decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                    new AgentPermissionRequest(principal, AgentPermission.ReadMetadata, AgentToolRisk.ReadOnly,
                        scope, policy.Revision, profile.IsReadOnly, context, generationId, destination, outputScope),
                    cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
            }
            if (decision.PolicyRevision != policy.Revision)
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
            if (!decision.IsAllowed)
            {
                if (decision.Reason == AgentPermissionDenialReason.MissingGrant) continue;
                return AgentToolInvocationResult.Failure(PermissionDenied, MapDenialReason(decision.Reason));
            }
            allowed.Add(item);
        }

        // The offset indexes authorized names, not the server's untrusted/unfiltered list.
        allowed.Sort(StringComparer.Ordinal);
        var page = allowed.Skip(skip).Take(MaximumConnections).ToArray();
        var truncated = allowed.Count > skip + page.Length;

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } failure)
            return AgentToolInvocationResult.Failure(PermissionDenied, failure);
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
        var json = JsonSerializer.Serialize(new NamesResponse(page, truncated), SerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json);
    }

    private async Task<AgentAuditDecisionReason?> RevalidateMetadataProfileAsync(
        ConnectionProfile expected, CancellationToken cancellationToken)
    {
        try
        {
            var profiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (profiles is null) return AgentAuditDecisionReason.ExecutionFailed;
            var matching = profiles.Where(item => item?.Id == expected.Id).Take(2).ToArray();
            return matching.Length == 1 && matching[0] == expected
                ? null : AgentAuditDecisionReason.ValidationRejected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentAuditDecisionReason.ExecutionFailed;
        }
    }

    private static bool IsSafeMetadataName(string? name)
    {
        if (name is not { Length: > 0 and <= 255 } || Utf8ByteCount(name) > 255 ||
            string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
            name.Any(c => char.IsControl(c) || c is '/' or '\\' or '@')) return false;
        var remaining = name.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    private static bool TryParseMetadataArguments(string name, string? json, out Guid connectionId, out string? database, out int skip)
    {
        connectionId = Guid.Empty;
        database = null;
        skip = 0;
        if (name == ListConnectionsToolName) return IsClosedEmptyObject(json);
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var count = 0;
            var hasSkip = false;
            foreach (var property in root.EnumerateObject())
            {
                count++;
                if (property.NameEquals("connectionId") && property.Value.ValueKind == JsonValueKind.String &&
                    Guid.TryParseExact(property.Value.GetString(), "D", out var parsed) && parsed != Guid.Empty)
                    connectionId = parsed;
                else if (name == ListCollectionsToolName && property.NameEquals("database") &&
                    property.Value.ValueKind == JsonValueKind.String && IsSafeMetadataName(property.Value.GetString()))
                    database = property.Value.GetString();
                else if (property.NameEquals("skip") && !hasSkip &&
                    property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var parsedSkip) &&
                    parsedSkip is >= 0 and <= 10_000)
                {
                    skip = parsedSkip;
                    hasSkip = true;
                }
                else return false;
            }
            return connectionId != Guid.Empty &&
                (name == ListDatabasesToolName && count == 1 + (hasSkip ? 1 : 0) ||
                 name == ListCollectionsToolName && count == 2 + (hasSkip ? 1 : 0) && database is not null);
        }
        catch (JsonException) { return false; }
    }

    private sealed record NamesResponse(
        [property: JsonPropertyName("names")] IReadOnlyList<string> Names,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private static int Utf8ByteCount(string value) => Encoding.UTF8.GetByteCount(value);

    private async Task<AgentAuditDecisionReason?> RevalidateProfilesAsync(
        IReadOnlyDictionary<Guid, AuthorizedConnectionSnapshot> expected,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (current is null) return AgentAuditDecisionReason.ExecutionFailed;
            var remaining = new HashSet<Guid>(expected.Keys);
            foreach (var profile in current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (profile is null || profile.Id == Guid.Empty ||
                    profile.SourceGenerationId is not Guid generationId || generationId == Guid.Empty ||
                    !IsValidProfileName(profile.Name))
                    return AgentAuditDecisionReason.ValidationRejected;
                if (!expected.TryGetValue(profile.Id, out var snapshot)) continue;
                if (!remaining.Remove(profile.Id) || generationId != snapshot.SourceGenerationId ||
                    !string.Equals(profile.Name, snapshot.OriginalName, StringComparison.Ordinal) ||
                    profile.IsReadOnly != snapshot.Summary.ReadOnly)
                    return AgentAuditDecisionReason.ValidationRejected;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return remaining.Count == 0 ? null : AgentAuditDecisionReason.ValidationRejected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentAuditDecisionReason.ExecutionFailed;
        }
    }

    private static bool IsCompleteInvocationContext(AgentInvocationContext? context) =>
        context is not null && context.SessionId is { } sessionId && sessionId != Guid.Empty &&
        context.TurnId is { } turnId && turnId != Guid.Empty;

    private static bool IsValidDestination(AgentOutputDestination destination, AgentInvocationContext context) =>
        Enum.IsDefined(destination.Kind) &&
        (destination.Kind == AgentOutputDestinationKind.Local && destination.ProviderId is null ||
         destination.Kind == AgentOutputDestinationKind.ProviderExternal &&
         IsSafeAuditExternalIdentifier(destination.ProviderId) &&
         string.Equals(destination.ProviderId, context.ProviderId, StringComparison.Ordinal));

    private static bool IsSafeAuditExternalIdentifier(string? value) =>
        value is { Length: > 0 and <= 64 } &&
        (char.IsAsciiLetterLower(value[0]) || char.IsAsciiDigit(value[0])) &&
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '.' or '-');

    private static bool IsValidProfileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) return false;
        var remaining = name.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    // A profile generation pins the template, not values resolved from ENV or the vault.
    // Reads that dispatch to MongoDB require a stable target for their namespace grant.
    private static bool HasDynamicMongoTarget(ConnectionProfile profile) =>
        profile.ConnectionString.Contains("${", StringComparison.Ordinal) ||
        profile.ConnectionString.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) ||
        profile.TargetHost?.Contains("${", StringComparison.Ordinal) == true ||
        profile.TargetHost?.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) == true;

    private static AgentAuditDecisionReason MapDenialReason(AgentPermissionDenialReason reason) => reason switch
    {
        AgentPermissionDenialReason.PolicyUnavailable or AgentPermissionDenialReason.InvalidPolicy =>
            AgentAuditDecisionReason.PolicyUnavailable,
        AgentPermissionDenialReason.PolicyRevisionMismatch or AgentPermissionDenialReason.InvalidPolicyRevision =>
            AgentAuditDecisionReason.PolicyRevisionMismatch,
        _ => AgentAuditDecisionReason.PermissionMissing
    };

    private readonly record struct PolicyLoadResult(
        AgentAuthorizationPolicySnapshot? Policy, AgentAuditDecisionReason DenialReason);

    private async Task<PolicyLoadResult> LoadCurrentPolicyAsync(AgentPrincipal principal, CancellationToken cancellationToken)
    {
        AgentAuthorizationPolicySnapshot? policy;
        try
        {
            policy = await AwaitWithCancellationAsync(_policies.LoadAsync(principal.Id, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(null, AgentAuditDecisionReason.PolicyUnavailable);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (policy is null) return new(null, AgentAuditDecisionReason.PolicyMissing);
        if (!policy.IsValid || policy.PrincipalId != principal.Id)
            return new(null, AgentAuditDecisionReason.PolicyUnavailable);
        if (policy.Revision != principal.PolicyRevision)
            return new(null, AgentAuditDecisionReason.PolicyRevisionMismatch);
        return new(policy, AgentAuditDecisionReason.PolicyAllowed);
    }

    private async Task<T> AwaitWithCancellationAsync<T>(Task<T> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            return await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HoldQuotaUntilCompletion(operation);

            throw;
        }
    }

    private async Task AwaitWithCancellationAsync(Task operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HoldQuotaUntilCompletion(operation);
            throw;
        }
    }

    private void HoldQuotaUntilCompletion(Task operation)
    {
        if (_activeQuotaLease.Value is { } lease)
            lease.HoldUntil(operation);
        else
            _ = operation.ContinueWith(static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private static bool IsClosedEmptyObject(string? json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record ListConnectionsResponse(
        [property: JsonPropertyName("connections")] IReadOnlyList<ConnectionSummary> Connections,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private sealed record ConnectionSummary(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("readOnly")] bool ReadOnly);

    private sealed record AuthorizedConnectionSnapshot(Guid SourceGenerationId, string OriginalName, ConnectionSummary Summary);
}
