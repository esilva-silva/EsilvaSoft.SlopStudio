using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Executes literal EJSON find queries without the trusted console's ENV or constructor rewriting.</summary>
public sealed class MongoAgentFindSource(
    IConnectionSecretStore secrets,
    IEnvironmentVaultRepository? environments,
    MongoClientPool clients) : IAgentMongoFindSource
{
    private const int MaximumDocumentBytes = 256 * 1024;
    private static readonly JsonWriterSettings CanonicalJson = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson
    };

    public async Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 100 || query.Skip is < 0 or > 10_000 ||
            query.MaxTimeMs is < 1 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(query));

        var filter = ParseLiteral(query.FilterEjson);
        var projection = query.ProjectionEjson is null ? null : ParseLiteral(query.ProjectionEjson);
        var sort = query.SortEjson is null ? null : ParseLiteral(query.SortEjson);
        // The operation context resolves only the connection string. Its dynamic JSON parser is never used here.
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients,
            cancellationToken).ConfigureAwait(false);
        var database = context.CreateClient().GetDatabase(query.Database);
        var originalUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, query.Collection,
            cancellationToken).ConfigureAwait(false);
        if (originalUuid is null) return new([], false, false, false, false);

        var options = new FindOptions<BsonDocument>
        {
            Limit = query.Limit + 1,
            Skip = query.Skip,
            MaxTime = TimeSpan.FromMilliseconds(query.MaxTimeMs),
            Projection = projection,
            Sort = sort,
            BatchSize = 1,
            Comment = "slopstudio:agent-find"
        };
        var documents = new List<string>(query.Limit);
        var hasMore = false;
        var truncated = false;
        var resultTooLarge = false;
        var projectedBytes = 0;
        using (var cursor = await database.GetCollection<BsonDocument>(query.Collection)
                   .FindAsync(filter, options, cancellationToken).ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var document in cursor.Current)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (documents.Count == query.Limit)
                    {
                        hasMore = true;
                        break;
                    }
                    var length = document.ToBson().Length;
                    if (length > MaximumDocumentBytes || length > MaximumDocumentBytes - projectedBytes)
                    {
                        resultTooLarge = documents.Count == 0;
                        truncated = !resultTooLarge;
                        hasMore = truncated;
                        break;
                    }
                    var canonical = document.ToJson(CanonicalJson);
                    // Account for the wire representation too. JSON escaping is checked again by the registry.
                    var canonicalBytes = System.Text.Encoding.UTF8.GetByteCount(canonical);
                    if (canonicalBytes > MaximumDocumentBytes - projectedBytes)
                    {
                        resultTooLarge = documents.Count == 0;
                        truncated = !resultTooLarge;
                        hasMore = truncated;
                        break;
                    }
                    projectedBytes += Math.Max(length, canonicalBytes);
                    documents.Add(canonical);
                }
                if (hasMore || resultTooLarge) break;
            }
        }

        var currentUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, query.Collection,
            cancellationToken).ConfigureAwait(false);
        if (currentUuid is null || !originalUuid.AsSpan().SequenceEqual(currentUuid))
            return new([], false, false, false, false);
        return new(documents, hasMore || documents.Count == query.Limit, truncated, true, resultTooLarge);
    }

    internal static BsonDocument ParseLiteral(string ejson)
    {
        using var parsed = JsonDocument.Parse(ejson, new JsonDocumentOptions { MaxDepth = 64 });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object || HasDuplicateProperties(parsed.RootElement))
            throw new FormatException("É necessário um objeto Extended JSON sem propriedades duplicadas.");
        var document = BsonDocument.Parse(ejson);
        if (ContainsCode(document)) throw new FormatException("Extended JSON com código não é permitido.");
        return document;
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(HasDuplicateProperties);
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property =>
            !seen.Add(property.Name) || HasDuplicateProperties(property.Value));
    }

    private static bool ContainsCode(BsonValue value) => value switch
    {
        BsonJavaScript or BsonJavaScriptWithScope => true,
        BsonDocument document => document.Elements.Any(element =>
            element.Name is "$where" or "$function" or "$accumulator" or "$eval" or "$lookup" or "$out" or "$merge" ||
            ContainsCode(element.Value)),
        BsonArray array => array.Any(ContainsCode),
        _ => false
    };
}
