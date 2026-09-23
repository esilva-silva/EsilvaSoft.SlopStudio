using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using System.Buffers;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Default-deny executable tool registry. The first handler only lists locally saved connections.</summary>
public sealed class AgentToolRegistry : IAgentToolRegistry
{
    public const string ListConnectionsToolName = "list_connections";
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

    private static readonly ReadOnlyCollection<AgentToolDescriptor> Descriptors = Array.AsReadOnly(
        [new AgentToolDescriptor(ListConnectionsToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata])]);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionProfileRepository _profiles;
    private readonly IAgentAuthorizationPolicyProvider _policies;
    private readonly IAgentPermissionEvaluator _permissions;
    private readonly TimeSpan _executionTimeout;

    public AgentToolRegistry(
        IConnectionProfileRepository profiles,
        IAgentAuthorizationPolicyProvider policies,
        IAgentPermissionEvaluator permissions,
        TimeSpan? executionTimeout = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        var requestedTimeout = executionTimeout ?? DefaultExecutionTimeout;
        if (requestedTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(executionTimeout));
        _executionTimeout = requestedTimeout > MaximumExecutionTimeout ? MaximumExecutionTimeout : requestedTimeout;
    }

    public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => Descriptors;

    public AgentToolDescriptor? FindDescriptor(string? name) =>
        string.Equals(name, ListConnectionsToolName, StringComparison.Ordinal) ? Descriptors[0] : null;

    public string? GetInputSchemaJson(string? name) =>
        string.Equals(name, ListConnectionsToolName, StringComparison.Ordinal) ? ListConnectionsInputSchema : null;

    public string? GetOutputSchemaJson(string? name) =>
        string.Equals(name, ListConnectionsToolName, StringComparison.Ordinal) ? ListConnectionsOutputSchema : null;

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
        try
        {
            return await InvokeWithinDeadlineAsync(principal, invocationContext, destination, outputDataScope, name, argumentsJson, deadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return AgentToolInvocationResult.Failure(DeadlineExceeded);
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
        if (!IsClosedEmptyObject(argumentsJson)) return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null) return AgentToolInvocationResult.Failure(PermissionDenied);
        if (!IsCompleteInvocationContext(invocationContext) || destination is null ||
            outputDataScope != AgentOutputDataScope.Metadata || !IsValidDestination(destination, invocationContext!))
            return AgentToolInvocationResult.Failure(PermissionDenied);

        // A valid current policy is checked before even enumerating local profile names.
        var initialPolicy = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (initialPolicy is null) return AgentToolInvocationResult.Failure(PermissionDenied);

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
            return AgentToolInvocationResult.Failure(PermissionDenied);
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
                return AgentToolInvocationResult.Failure(PermissionDenied);

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
                return AgentToolInvocationResult.Failure(PermissionDenied);
            }

            if (decision.PolicyRevision != initialPolicy.Revision)
                return AgentToolInvocationResult.Failure(PermissionDenied);
            if (!decision.IsAllowed)
            {
                if (decision.Reason == AgentPermissionDenialReason.MissingGrant) continue;
                return AgentToolInvocationResult.Failure(PermissionDenied);
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
        if (!await RevalidateProfilesAsync(authorizedSnapshots, cancellationToken).ConfigureAwait(false))
            return AgentToolInvocationResult.Failure(PermissionDenied);

        // Keep policy validation last, including revocation during the final profile read.
        var finalPolicy = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalPolicy is null || finalPolicy.Revision != initialPolicy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied);

        var output = new ListConnectionsResponse(authorized, truncated);
        var json = JsonSerializer.Serialize(output, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumOutputBytes)
            return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json);
    }

    private static int Utf8ByteCount(string value) => Encoding.UTF8.GetByteCount(value);

    private async Task<bool> RevalidateProfilesAsync(
        IReadOnlyDictionary<Guid, AuthorizedConnectionSnapshot> expected,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (current is null) return false;
            var remaining = new HashSet<Guid>(expected.Keys);
            foreach (var profile in current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (profile is null || profile.Id == Guid.Empty ||
                    profile.SourceGenerationId is not Guid generationId || generationId == Guid.Empty ||
                    !IsValidProfileName(profile.Name))
                    return false;
                if (!expected.TryGetValue(profile.Id, out var snapshot)) continue;
                if (!remaining.Remove(profile.Id) || generationId != snapshot.SourceGenerationId ||
                    !string.Equals(profile.Name, snapshot.OriginalName, StringComparison.Ordinal) ||
                    profile.IsReadOnly != snapshot.Summary.ReadOnly)
                    return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return remaining.Count == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static bool IsCompleteInvocationContext(AgentInvocationContext? context) =>
        context is not null && context.SessionId is { } sessionId && sessionId != Guid.Empty &&
        context.TurnId is { } turnId && turnId != Guid.Empty;

    private static bool IsValidDestination(AgentOutputDestination destination, AgentInvocationContext context) =>
        Enum.IsDefined(destination.Kind) &&
        (destination.Kind == AgentOutputDestinationKind.Local && destination.ProviderId is null ||
         destination.Kind == AgentOutputDestinationKind.ProviderExternal &&
         !string.IsNullOrWhiteSpace(destination.ProviderId) &&
         string.Equals(destination.ProviderId, context.ProviderId, StringComparison.Ordinal));

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

    private async Task<AgentAuthorizationPolicySnapshot?> LoadCurrentPolicyAsync(AgentPrincipal principal, CancellationToken cancellationToken)
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
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return policy is { IsValid: true }
            && policy.PrincipalId == principal.Id
            && policy.Revision == principal.PolicyRevision
            ? policy
            : null;
    }

    private static async Task<T> AwaitWithCancellationAsync<T>(Task<T> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            return await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = operation.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            throw;
        }
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
