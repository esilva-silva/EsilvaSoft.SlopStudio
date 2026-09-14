using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class ConsoleDatabaseSessionFactory(MongoClientPool? clients = null) : IConsoleDatabaseSessionFactory
{
    public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) =>
        new ConsoleDatabaseSession(resolvedProfiles, documentLimit, timeoutMs, clients ?? MongoClientPool.Shared);
}

internal sealed class ConsoleDatabaseSession(IReadOnlyList<ConnectionProfile> profiles, int documentLimit, int timeoutMs, MongoClientPool clients) : IConsoleDatabaseSession
{
    private readonly Dictionary<Guid, MongoClient> _clients = [];
    private static readonly JsonWriterSettings JsonSettings = new() { OutputMode = JsonOutputMode.CanonicalExtendedJson };
    public void Dispose() { _clients.Clear(); }

    public async Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken)
    {
        var profile = profiles.Single(p => p.Id == operation.ProfileId);
        var args = BsonSerializer.Deserialize<BsonArray>(operation.ArgumentsJson);
        Validate(operation, args, profile);
        if (!_clients.TryGetValue(profile.Id, out var client))
        {
            var uri = await ConnectionRouting.ApplyAsync(profile.ConnectionString, profile.TargetHost, cancellationToken).ConfigureAwait(false);
            var settings = MongoClientSettings.FromConnectionString(uri);
            settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(timeoutMs);
            client = clients.Get(settings); _clients.Add(profile.Id, client);
        }
        var db = client.GetDatabase(operation.Database);
        var collection = string.IsNullOrWhiteSpace(operation.Collection) ? null : db.GetCollection<BsonDocument>(operation.Collection);
        BsonDocument Doc(int index) => index < args.Count && !args[index].IsBsonNull ? args[index].AsBsonDocument : new();
        BsonValue value;
        switch (operation.Method)
        {
            case "find":
            {
                var options = Doc(2); var limit = BoundedLimit(options);
                var findOptions = new FindOptions { MaxTime = TimeSpan.FromMilliseconds(Math.Min(timeoutMs, NonNegative(options.GetValue("maxTimeMS", timeoutMs)))), BatchSize = Math.Clamp(NonNegative(options.GetValue("batchSize", 100)), 1, 1000) };
                if (options.TryGetValue("hint", out var hint)) findOptions.Hint = hint;
                if (options.TryGetValue("comment", out var comment)) findOptions.Comment = comment;
                if (options.TryGetValue("collation", out var collation)) findOptions.Collation = Collation.FromBsonDocument(collation.AsBsonDocument);
                var find = collection!.Find(Doc(0), findOptions);
                if (options.TryGetValue("sort", out var sort)) find = find.Sort(sort.AsBsonDocument);
                var projection = options.GetValue("project", args.Count > 1 ? args[1] : BsonNull.Value);
                if (!projection.IsBsonNull) find = find.Project<BsonDocument>(projection.AsBsonDocument);
                find = find.Skip(NonNegative(options.GetValue("skip", 0))).Limit(limit + 1);
                using var cursor = await find.ToCursorAsync(cancellationToken).ConfigureAwait(false);
                return await ReadPage(cursor, limit, cancellationToken).ConfigureAwait(false);
            }
            case "aggregate":
            {
                var options = Doc(1); var limit = BoundedLimit(options);
                var stages = args[0].AsBsonArray.Select(v => v.AsBsonDocument).ToList();
                if (options.TryGetValue("sort", out var sort)) stages.Add(new("$sort", sort));
                if (options.TryGetValue("skip", out var skip) && NonNegative(skip) > 0) stages.Add(new("$skip", skip));
                if (options.TryGetValue("project", out var projection)) stages.Add(new("$project", projection));
                stages.Add(new("$limit", limit + 1));
                using var cursor = await collection!.AggregateAsync<BsonDocument>(stages.ToArray(), new AggregateOptions { MaxTime = TimeSpan.FromMilliseconds(timeoutMs), BatchSize = 100 }, cancellationToken).ConfigureAwait(false);
                return await ReadPage(cursor, limit, cancellationToken).ConfigureAwait(false);
            }
            case "findOne":
            {
                var find = collection!.Find(Doc(0), new FindOptions { MaxTime = TimeSpan.FromMilliseconds(timeoutMs) });
                if (args.Count > 1 && !args[1].IsBsonNull) find = find.Project<BsonDocument>(Doc(1));
                value = (BsonValue?)await find.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? BsonNull.Value;
                break;
            }
            case "countDocuments":
                value = new BsonInt64(await collection!.CountDocumentsAsync(Doc(0), new CountOptions { MaxTime = TimeSpan.FromMilliseconds(timeoutMs) }, cancellationToken).ConfigureAwait(false)); break;
            case "estimatedDocumentCount":
                value = new BsonInt64(await collection!.EstimatedDocumentCountAsync(new EstimatedDocumentCountOptions { MaxTime = TimeSpan.FromMilliseconds(timeoutMs) }, cancellationToken).ConfigureAwait(false)); break;
            case "distinct":
            {
                using var cursor = await collection!.DistinctAsync<BsonValue>(args[0].AsString, Doc(1), new DistinctOptions { MaxTime = TimeSpan.FromMilliseconds(timeoutMs) }, cancellationToken).ConfigureAwait(false);
                return await ReadPage(cursor, documentLimit, cancellationToken).ConfigureAwait(false);
            }
            case "insertOne":
            {
                var document = Doc(0);
                await collection!.InsertOneAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
                value = new BsonDocument { ["acknowledged"] = true, ["insertedId"] = document.GetValue("_id", BsonNull.Value) }; break;
            }
            case "insertMany":
            {
                var documents = args[0].AsBsonArray.Select(v => v.AsBsonDocument).ToArray();
                if (documents.Length is < 1 or > 1000) throw new ArgumentException("Inserção exige entre 1 e 1000 documentos.");
                await collection!.InsertManyAsync(documents, cancellationToken: cancellationToken).ConfigureAwait(false);
                value = new BsonDocument { ["acknowledged"] = true, ["insertedIds"] = new BsonArray(documents.Select(d => d.GetValue("_id", BsonNull.Value))) }; break;
            }
            case "updateOne": case "updateMany":
            {
                var options = Doc(2); ValidateOptions(options, "upsert", "arrayFilters");
                var updateOptions = new UpdateOptions { IsUpsert = options.GetValue("upsert", false).ToBoolean() };
                if (options.TryGetValue("arrayFilters", out var arrayFilters)) updateOptions.ArrayFilters = arrayFilters.AsBsonArray.Select(v => new BsonDocumentArrayFilterDefinition<BsonDocument>(v.AsBsonDocument));
                UpdateDefinition<BsonDocument> update = args[1].IsBsonArray
                    ? new PipelineUpdateDefinition<BsonDocument>(args[1].AsBsonArray.Select(v => v.AsBsonDocument).ToArray()) : Doc(1);
                var result = operation.Method == "updateMany"
                    ? await collection!.UpdateManyAsync(Doc(0), update, updateOptions, cancellationToken).ConfigureAwait(false)
                    : await collection!.UpdateOneAsync(Doc(0), update, updateOptions, cancellationToken).ConfigureAwait(false);
                value = new BsonDocument { ["acknowledged"] = result.IsAcknowledged, ["matchedCount"] = result.MatchedCount, ["modifiedCount"] = result.ModifiedCount, ["upsertedId"] = result.UpsertedId ?? BsonNull.Value }; break;
            }
            case "replaceOne":
            {
                var options = Doc(2); ValidateOptions(options, "upsert");
                var result = await collection!.ReplaceOneAsync(Doc(0), Doc(1), new ReplaceOptions { IsUpsert = options.GetValue("upsert", false).ToBoolean() }, cancellationToken).ConfigureAwait(false);
                value = new BsonDocument { ["acknowledged"] = result.IsAcknowledged, ["matchedCount"] = result.MatchedCount, ["modifiedCount"] = result.ModifiedCount }; break;
            }
            case "deleteOne": case "deleteMany":
            {
                var result = operation.Method == "deleteMany" ? await collection!.DeleteManyAsync(Doc(0), cancellationToken).ConfigureAwait(false)
                    : await collection!.DeleteOneAsync(Doc(0), cancellationToken).ConfigureAwait(false);
                value = new BsonDocument { ["acknowledged"] = result.IsAcknowledged, ["deletedCount"] = result.DeletedCount }; break;
            }
            case "drop": await db.DropCollectionAsync(operation.Collection, cancellationToken).ConfigureAwait(false); value = BsonBoolean.True; break;
            case "dropDatabase": await client.DropDatabaseAsync(operation.Database, cancellationToken).ConfigureAwait(false); value = BsonBoolean.True; break;
            case "dropIndex": await collection!.Indexes.DropOneAsync(args[0].AsString, cancellationToken).ConfigureAwait(false); value = BsonBoolean.True; break;
            case "createIndex":
            {
                var options = Doc(1);
                // A fixed command shape preserves MongoDB index options without exposing arbitrary runCommand.
                var definition = new BsonDocument("key", Doc(0)); definition.AddRange(options);
                if (!definition.Contains("name")) definition["name"] = string.Join("_", Doc(0).Elements.Select(e => e.Name + "_" + e.Value));
                value = await db.RunCommandAsync<BsonDocument>(new BsonDocument { ["createIndexes"] = operation.Collection, ["indexes"] = new BsonArray { definition } }, cancellationToken: cancellationToken).ConfigureAwait(false); break;
            }
            case "createCollection":
            {
                var command = new BsonDocument("create", operation.Collection); command.AddRange(Doc(0));
                value = await db.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false); break;
            }
            case "stats": value = await db.RunCommandAsync<BsonDocument>(new BsonDocument("collStats", operation.Collection), cancellationToken: cancellationToken).ConfigureAwait(false); break;
            case "databaseStats": value = await db.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1), cancellationToken: cancellationToken).ConfigureAwait(false); break;
            default: throw new InvalidOperationException("Método não suportado: " + operation.Method);
        }
        return Reply(value);
    }

    internal static void Validate(ConsoleOperation operation, BsonArray args, ConnectionProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Database);
        var read = operation.Method is "find" or "findOne" or "countDocuments" or "estimatedDocumentCount" or "distinct" or "aggregate" or "stats" or "databaseStats";
        if (!read)
        {
            profile.EnsureWriteAllowed();
            if (operation.Database is "admin" or "local" or "config" || operation.Collection.StartsWith("system.", StringComparison.Ordinal)) throw new InvalidOperationException("Destino de sistema protegido.");
        }
        if (operation.Method is "updateOne" or "updateMany" or "replaceOne" or "deleteOne" or "deleteMany")
            if (args.Count == 0 || !args[0].IsBsonDocument || args[0].AsBsonDocument.ElementCount == 0) throw new InvalidOperationException("Uma operação de alteração exige filtro não vazio.");
        if (operation.Method == "dropIndex" && (args.Count == 0 || args[0] == "_id_" || args[0] == "*")) throw new InvalidOperationException("Índice obrigatório ou remoção global protegida.");
        if (operation.Method == "aggregate" && args[0].AsBsonArray.Any(HasWriteStage)) throw new InvalidOperationException("$out e $merge não são suportados no cursor de leitura.");
    }
    private static bool HasWriteStage(BsonValue value) => value.IsBsonArray ? value.AsBsonArray.Any(HasWriteStage) : value.IsBsonDocument &&
        value.AsBsonDocument.Elements.Any(e => e.Name is "$out" or "$merge" || HasWriteStage(e.Value));
    private static void ValidateOptions(BsonDocument options, params string[] allowed)
    {
        var unknown = options.Names.FirstOrDefault(n => !allowed.Contains(n, StringComparer.Ordinal));
        if (unknown is not null) throw new ArgumentException("Opção não suportada: " + unknown);
    }
    private int BoundedLimit(BsonDocument options) { var value = NonNegative(options.GetValue("limit", documentLimit)); return Math.Min(value == 0 ? documentLimit : value, documentLimit); }
    private static int NonNegative(BsonValue value) => value.IsNumeric && value.ToDouble() == Math.Truncate(value.ToDouble()) && value.ToDouble() is >= 0 and <= int.MaxValue
        ? value.ToInt32() : throw new ArgumentException("Skip/limit exige inteiro não negativo.");
    private static string Reply(BsonValue value, bool truncated = false) => new BsonDocument { ["value"] = value, ["truncated"] = truncated }.ToJson(JsonSettings);
    private static async Task<string> ReadPage<T>(IAsyncCursor<T> cursor, int limit, CancellationToken token) where T : BsonValue
    {
        var values = new BsonArray(); var size = 0;
        while (await cursor.MoveNextAsync(token).ConfigureAwait(false))
        {
            foreach (var value in cursor.Current)
            {
                if (values.Count == limit) return Reply(values, true);
                size += value.ToJson(JsonSettings).Length;
                if (size > 4_000_000) return Reply(values, true);
                values.Add(value);
            }
        }
        return Reply(values);
    }
}
