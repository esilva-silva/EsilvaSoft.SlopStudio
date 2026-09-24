using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Reads only an explicit allowlist of index metadata; BSON option values never leave this adapter.</summary>
public sealed class MongoAgentIndexSource(
    IConnectionSecretStore secrets,
    IEnvironmentVaultRepository? environments,
    MongoClientPool clients) : IAgentMongoIndexSource
{
    private const int MaximumIndexes = 200;
    private const int MaximumRawIndexBytes = 64 * 1024;
    private const int MaximumRawTotalBytes = 256 * 1024;
    private const int MaximumDefinitionFields = 32;
    private const int MaximumKeyFields = 32;

    public async Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
        string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        if (maximumExecutionTime <= TimeSpan.Zero || maximumExecutionTime > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(maximumExecutionTime));
        using var sourceDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sourceDeadline.CancelAfter(maximumExecutionTime);
        var operationToken = sourceDeadline.Token;
        if (profile.ConnectionString.Contains("${", StringComparison.Ordinal) ||
            profile.ConnectionString.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) ||
            profile.TargetHost?.Contains("${", StringComparison.Ordinal) == true ||
            profile.TargetHost?.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("Dynamic targets are not supported for agent index reads.");
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients,
            operationToken).ConfigureAwait(false);
        var target = context.CreateClient().GetDatabase(database);
        var originalUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(target, collection,
            operationToken).ConfigureAwait(false);
        if (originalUuid is null) return new([], false, false);

        var indexes = new List<AgentMongoIndexSummary>(MaximumIndexes);
        var truncated = false;
        var rawBytes = 0;
        // One definition per batch bounds the cursor's decoded batch; MongoDB may still send one
        // oversized definition, which is rejected before it enters the projected result.
        var options = CreateListOptions(maximumExecutionTime);
        using (var cursor = await target.GetCollection<BsonDocument>(collection).Indexes.ListAsync(options, operationToken)
                   .ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync(operationToken).ConfigureAwait(false))
            {
                foreach (var definition in cursor.Current)
                {
                    operationToken.ThrowIfCancellationRequested();
                    if (indexes.Count == MaximumIndexes)
                    {
                        truncated = true;
                        break;
                    }
                    var projected = ProjectBounded(definition, MaximumRawTotalBytes - rawBytes,
                        out var definitionBytes);
                    rawBytes += definitionBytes;
                    indexes.Add(projected);
                }
                if (truncated) break;
            }
        }

        var currentUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(target, collection,
            operationToken).ConfigureAwait(false);
        if (currentUuid is null || !originalUuid.AsSpan().SequenceEqual(currentUuid))
            return new([], false, false);
        return new(indexes, truncated, true);
    }

    internal static AgentMongoIndexSummary Project(BsonDocument definition)
    {
        if (!definition.TryGetValue("name", out var name) || !name.IsString ||
            !definition.TryGetValue("key", out var key) || key is not BsonDocument fields)
            throw new FormatException("Invalid index metadata.");
        return new(name.AsString, fields.Names.ToArray(), Flag(definition, "unique"),
            Flag(definition, "sparse"), Flag(definition, "hidden"));
    }

    internal static ListIndexesOptions CreateListOptions(TimeSpan maximumExecutionTime)
    {
        if (maximumExecutionTime <= TimeSpan.Zero || maximumExecutionTime > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(maximumExecutionTime));
        var options = new ListIndexesOptions { BatchSize = 1 };
        // The 3.11.1 XML documents Timeout, but its net6.0 binary does not expose it.
        // Set it when supplied by the loaded driver; the linked token enforces the same
        // deadline on the current binary and across all calls in this operation.
        var timeout = typeof(ListIndexesOptions).GetProperty("Timeout");
        if (timeout?.CanWrite == true &&
            (timeout.PropertyType == typeof(TimeSpan) ||
             Nullable.GetUnderlyingType(timeout.PropertyType) == typeof(TimeSpan)))
            timeout.SetValue(options, maximumExecutionTime);
        return options;
    }

    internal static AgentMongoIndexSummary ProjectBounded(BsonDocument definition, int remainingBytes,
        out int definitionBytes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.ElementCount is < 2 or > MaximumDefinitionFields ||
            !definition.TryGetValue("key", out var key) || key is not BsonDocument fields ||
            fields.ElementCount is < 1 or > MaximumKeyFields)
            throw new FormatException("Index metadata exceeds the field limit.");
        definitionBytes = definition.ToBson().Length;
        if (definitionBytes > MaximumRawIndexBytes || definitionBytes > remainingBytes)
            throw new FormatException("Index metadata exceeds the byte limit.");
        return Project(definition);
    }

    private static bool Flag(BsonDocument definition, string name) =>
        definition.TryGetValue(name, out var value) && value.IsBoolean && value.AsBoolean;
}
