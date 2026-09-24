using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    private async Task<AgentToolInvocationResult> InvokeFindAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseFindArguments(argumentsJson, out var connectionId, out var database, out var collection, out var query))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentOutputDataScope.DocumentValues ||
            _find is null)
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        var generationId = profile.SourceGenerationId!.Value;
        var scope = AgentNamespaceScope.ForCollection(connectionId, database!, collection!);
        foreach (var permission in new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments })
        {
            AgentPermissionDecision decision;
            try
            {
                decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                    new AgentPermissionRequest(principal, permission, AgentToolRisk.ReadOnly, scope,
                        policy.Revision, profile.IsReadOnly, context, generationId, destination, outputScope),
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
                return AgentToolInvocationResult.Failure(PermissionDenied, MapDenialReason(decision.Reason));
        }

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } preflightFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, preflightFailure);
        var preflightPolicy = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (preflightPolicy.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, preflightPolicy.DenialReason);
        if (preflightPolicy.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        AgentMongoFindPage page;
        try
        {
            var allowedTimeMs = (int)Math.Clamp(_executionTimeout.TotalMilliseconds, 1, 30_000);
            page = await AwaitWithCancellationAsync(_find.FindAsync(profile,
                query! with { MaxTimeMs = Math.Min(query!.MaxTimeMs, allowedTimeMs) }, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FormatException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(InvalidArguments);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }
        if (page is null || !page.TargetVerified || page.DocumentsEjson is null ||
            page.DocumentsEjson.Count > query!.Limit || page.Truncated && !page.HasMore)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        if (page.ResultTooLarge) return AgentToolInvocationResult.Failure(ResultTooLarge);

        var returned = new List<string>(page.DocumentsEjson.Count);
        var truncated = page.Truncated;
        foreach (var document in page.DocumentsEjson)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document is null || Utf8ByteCount(document) > MaximumOutputBytes ||
                !IsLiteralEjsonDocument(document, rejectCode: false, maximumBytes: MaximumOutputBytes))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            var candidate = new FindResponse([.. returned, document], returned.Count + 1,
                true, true, "OutputLimit");
            if (Utf8ByteCount(JsonSerializer.Serialize(candidate, SerializerOptions)) > MaximumOutputBytes)
            {
                if (returned.Count == 0) return AgentToolInvocationResult.Failure(ResultTooLarge);
                truncated = true;
                break;
            }
            returned.Add(document);
        }

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } outputFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, outputFailure);
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var json = JsonSerializer.Serialize(new FindResponse(returned, returned.Count,
            page.HasMore || truncated, truncated, truncated ? "OutputLimit" : null), SerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json);
    }

    private static bool TryParseFindArguments(string? json, out Guid connectionId, out string? database,
        out string? collection, out AgentMongoFindQuery? query)
    {
        connectionId = Guid.Empty;
        database = null;
        collection = null;
        query = null;
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var found = new HashSet<string>(StringComparer.Ordinal);
            var filter = "{}";
            string? projection = null;
            string? sort = null;
            var limit = 20;
            var skip = 0;
            var maxTimeMs = 5_000;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!found.Add(property.Name)) return false;
                switch (property.Name)
                {
                    case "connectionId" when property.Value.ValueKind == JsonValueKind.String &&
                        Guid.TryParseExact(property.Value.GetString(), "D", out var parsedConnection) &&
                        parsedConnection != Guid.Empty:
                        connectionId = parsedConnection;
                        break;
                    case "database" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSafeMetadataName(property.Value.GetString()):
                        database = property.Value.GetString();
                        break;
                    case "collection" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSafeMetadataName(property.Value.GetString()):
                        collection = property.Value.GetString();
                        break;
                    case "filterEjson" when property.Value.ValueKind == JsonValueKind.String &&
                        IsLiteralEjsonDocument(property.Value.GetString(), rejectCode: true):
                        filter = property.Value.GetString()!;
                        break;
                    case "projectionEjson" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSimpleFieldSpec(property.Value.GetString(), allowDescending: false):
                        projection = property.Value.GetString();
                        break;
                    case "sortEjson" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSimpleFieldSpec(property.Value.GetString(), allowDescending: true):
                        sort = property.Value.GetString();
                        break;
                    case "limit" when property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out var parsedLimit) && parsedLimit is >= 1 and <= 100:
                        limit = parsedLimit;
                        break;
                    case "skip" when property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out var parsedSkip) && parsedSkip is >= 0 and <= 10_000:
                        skip = parsedSkip;
                        break;
                    case "maxTimeMs" when property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out var parsedMaxTime) && parsedMaxTime is >= 1 and <= 30_000:
                        maxTimeMs = parsedMaxTime;
                        break;
                    default: return false;
                }
            }
            if (connectionId == Guid.Empty || database is null || collection is null) return false;
            query = new(database, collection, filter, projection, sort, limit, skip, maxTimeMs);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsLiteralEjsonDocument(string? json, bool rejectCode, int maximumBytes = MaximumInputBytes)
    {
        if (json is null || json.Length > maximumBytes || Utf8ByteCount(json) > maximumBytes)
            return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                (!rejectCode || !HasForbiddenOperator(document.RootElement));
        }
        catch (JsonException) { return false; }
    }

    private static bool HasForbiddenOperator(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(HasForbiddenOperator);
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || property.Name is "$where" or "$function" or "$accumulator" or
                    "$code" or "$eval" or "$lookup" or "$out" or "$merge" ||
                HasForbiddenOperator(property.Value)) return true;
        }
        return false;
    }

    private static bool IsSimpleFieldSpec(string? json, bool allowDescending)
    {
        if (!IsLiteralEjsonDocument(json, rejectCode: true)) return false;
        using var document = JsonDocument.Parse(json!);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!seen.Add(property.Name) || property.Name.Length is < 1 or > 255 ||
                property.Name.Contains('$') || property.Name.StartsWith('.') || property.Name.EndsWith('.') ||
                property.Name.Contains("..", StringComparison.Ordinal) || property.Name.Any(char.IsControl))
                return false;
            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt32(out var direction) ||
                direction is not (0 or 1) && (!allowDescending || direction != -1) ||
                allowDescending && direction == 0) return false;
        }
        return true;
    }

    private sealed record FindResponse(
        [property: JsonPropertyName("documentsEjson")] IReadOnlyList<string> DocumentsEjson,
        [property: JsonPropertyName("returnedCount")] int ReturnedCount,
        [property: JsonPropertyName("hasMore")] bool HasMore,
        [property: JsonPropertyName("truncated")] bool Truncated,
        [property: JsonPropertyName("truncationReason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TruncationReason);
}
