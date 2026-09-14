using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class MongoWorkspaceService : IMongoWorkspaceService
{
    private readonly IConnectionSecretStore _secrets;
    private readonly MongoClientPool _clients;
    private readonly IEnvironmentVaultRepository? _environments;
    private readonly AsyncLocal<OperationEnvironment?> _operation = new();
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };
    private static readonly JsonWriterSettings CanonicalJsonSettings = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson,
        Indent = true
    };

    public MongoWorkspaceService(IConnectionSecretStore? secrets = null, IEnvironmentVaultRepository? environments = null, MongoClientPool? clients = null)
    {
        _secrets = secrets ?? new SessionConnectionSecretStore();
        _environments = environments;
        _clients = clients ?? MongoClientPool.Shared;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var client = CreateClient(profile);
            var response = await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var version = response.TryGetValue("version", out var value) ? value.AsString : null;

            return new ConnectionTestResult(true, "Conexão estabelecida.", Stopwatch.GetElapsedTime(startedAt), version);
        }
        catch (Exception exception) when (exception is MongoException or TimeoutException or FormatException)
        {
            return new ConnectionTestResult(false, OperationErrorMessages.Describe(exception), Stopwatch.GetElapsedTime(startedAt));
        }
    }

    public async Task<IReadOnlyList<string>> GetDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var client = CreateClient(profile);
        using var cursor = await client.ListDatabaseNamesAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<string>();

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            result.AddRange(cursor.Current);
        }

        return result.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var collectionCursor = await CreateClient(profile)
            .GetDatabase(database)
            .ListCollectionNamesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (collectionCursor)
        {
            var result = new List<string>();

            while (await collectionCursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                result.AddRange(collectionCursor.Current);
            }

            return result.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public async Task CreateDatabaseAsync(ConnectionProfile profile, DatabaseCreateRequest request, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var environment = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await environment.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await CreateClient(profile, environment)
            .GetDatabase(request.Database)
            .CreateCollectionAsync(request.InitialCollection, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateCollectionAsync(ConnectionProfile profile, CollectionCreateRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var database = CreateClient(profile).GetDatabase(request.Database);
        if (!string.IsNullOrWhiteSpace(request.ViewOn))
        {
            var command = new BsonDocument
            {
                ["create"] = request.Collection,
                ["viewOn"] = request.ViewOn,
                ["pipeline"] = new BsonArray(ParsePipeline(request.ViewPipelineJson!))
            };
            var collation = ParseOptionalDocument(request.CollationJson, "collation");
            if (collation is not null)
            {
                command["collation"] = collation;
            }
            await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await database.CreateCollectionAsync(
            request.Collection,
            new CreateCollectionOptions<BsonDocument>
            {
                Capped = request.IsCapped,
                MaxSize = request.MaxSizeBytes,
                MaxDocuments = request.MaxDocuments,
                Collation = ParseOptionalCollation(request.CollationJson),
                ClusteredIndex = request.IsClustered
                    ? new ClusteredIndexOptions<BsonDocument>
                    {
                        Key = new BsonDocumentIndexKeysDefinition<BsonDocument>(ParseDocument(request.ClusteredIndexKeyJson!, "chave clustered")),
                        Unique = true
                    }
                    : null
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameCollectionAsync(ConnectionProfile profile, CollectionRenameRequest request, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var environment = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await environment.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await CreateClient(profile, environment)
            .GetDatabase(request.Database)
            .RenameCollectionAsync(request.SourceCollection, request.TargetCollection, new RenameCollectionOptions { DropTarget = request.DropTarget }, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateViewAsync(ConnectionProfile profile, ViewUpdateRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var command = new BsonDocument
        {
            ["collMod"] = request.View,
            ["pipeline"] = new BsonArray(ParsePipeline(request.PipelineJson))
        };
        await CreateClient(profile)
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ConfigureCollectionValidationAsync(ConnectionProfile profile, CollectionValidationRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var command = new BsonDocument
        {
            ["collMod"] = request.Collection,
            ["validator"] = ParseDocument(request.ValidatorJson, "validador"),
            ["validationLevel"] = request.ValidationLevel.ToString().ToLowerInvariant(),
            ["validationAction"] = request.ValidationAction.ToString().ToLowerInvariant()
        };
        await CreateClient(profile)
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CollectionValidationInfo> GetCollectionValidationAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        EnsureUserCollection(database, collection);
        using var cursor = await CreateClient(profile)
            .GetDatabase(database)
            .ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", collection) }, cancellationToken)
            .ConfigureAwait(false);
        var definition = (await cursor.ToListAsync(cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        if (definition is null)
        {
            throw new InvalidOperationException($"A coleção {collection} não foi encontrada.");
        }

        var options = definition.GetValue("options", new BsonDocument()).AsBsonDocument;
        var validator = options.GetValue("validator", new BsonDocument());
        var validatorJson = validator.IsBsonDocument ? validator.AsBsonDocument.ToJson(CanonicalJsonSettings) : "{}";
        var level = ParseValidationLevel(options.GetValue("validationLevel", "strict").AsString);
        var action = ParseValidationAction(options.GetValue("validationAction", "error").AsString);
        return new CollectionValidationInfo(validatorJson, level, action);
    }

    public async Task DropCollectionAsync(ConnectionProfile profile, CollectionDropRequest request, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var environment = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await environment.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await CreateClient(profile, environment)
            .GetDatabase(request.Database)
            .DropCollectionAsync(request.Collection, cancellationToken).ConfigureAwait(false);
    }

    public async Task DropDatabaseAsync(ConnectionProfile profile, DatabaseDropRequest request, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var environment = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await environment.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await CreateClient(profile, environment).DropDatabaseAsync(request.Database, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetServerStatusAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var status = await CreateClient(profile)
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("serverStatus", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return status.ToJson(CanonicalJsonSettings);
    }

    public async Task<string> GetTopologyAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var topology = await CreateClient(profile)
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return topology.ToJson(CanonicalJsonSettings);
    }

    public async Task<string> GetCurrentOperationsAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var operations = await CreateClient(profile)
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    ["currentOp"] = 1,
                    ["allUsers"] = true
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return operations.ToJson(CanonicalJsonSettings);
    }

    public async Task<string> GetProfilerStatusAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var status = await CreateClient(profile)
            .GetDatabase(database)
            .RunCommandAsync<BsonDocument>(new BsonDocument("profile", -1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return status.ToJson(CanonicalJsonSettings);
    }

    public async Task KillOperationAsync(ConnectionProfile profile, OperationKillRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        var operationId = request.GetOperationId();
        await CreateClient(profile)
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("killOp", operationId), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> ValidateCollectionIntegrityAsync(ConnectionProfile profile, CollectionIntegrityCheckRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var result = await CreateClient(profile)
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    ["validate"] = request.Collection,
                    ["full"] = true
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.ToJson(CanonicalJsonSettings);
    }

    public async Task<string> CompactCollectionAsync(ConnectionProfile profile, CollectionCompactRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var command = new BsonDocument("compact", request.Collection);
        if (request.Force)
        {
            command["force"] = true;
        }

        var result = await CreateClient(profile)
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.ToJson(CanonicalJsonSettings);
    }

    public async Task<string> GetUsersAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var users = await CreateClient(profile)
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("usersInfo", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return users.ToJson(CanonicalJsonSettings);
    }

    public async Task CreateUserAsync(ConnectionProfile profile, DatabaseUserCreateRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var roles = ParsePipeline(request.RolesJson);
        var command = new BsonDocument
        {
            ["createUser"] = request.Username.Trim(),
            ["pwd"] = request.Password,
            ["roles"] = new BsonArray(roles.Select(role => (BsonValue)role))
        };
        await CreateClient(profile)
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DropUserAsync(ConnectionProfile profile, DatabaseUserDropRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        await CreateClient(profile)
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(
                new BsonDocument("dropUser", request.Username.Trim()),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateUserRolesAsync(ConnectionProfile profile, DatabaseUserRoleRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var commandName = request.Revoke ? "revokeRolesFromUser" : "grantRolesToUser";
        var command = new BsonDocument
        {
            [commandName] = request.Username.Trim(),
            ["roles"] = new BsonArray(ParsePipeline(request.RolesJson).Select(role => (BsonValue)role))
        };
        await CreateClient(profile)
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> GetRolesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var roles = await CreateClient(profile)
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("rolesInfo", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return roles.ToJson(CanonicalJsonSettings);
    }

    public async Task<string> GetDatabaseStatsAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var stats = await CreateClient(profile)
            .GetDatabase(database)
            .RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return stats.ToJson(CanonicalJsonSettings);
    }

    public async Task<string> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        using var cursor = await CreateClient(profile).GetDatabase(database).ListCollectionsAsync(
            new ListCollectionsOptions { Filter = new BsonDocument("name", collection) }, cancellationToken).ConfigureAwait(false);
        var entries = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        return entries.FirstOrDefault()?.ToJson(CanonicalJsonSettings) ?? "{}";
    }

    public async Task<string> GetCollectionStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        var stats = await CreateClient(profile)
            .GetDatabase(database)
            .RunCommandAsync<BsonDocument>(new BsonDocument("collStats", collection), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return stats.ToJson(CanonicalJsonSettings);
    }

    public async Task<QueryPage> QueryAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        query.Validate();
        var startedAt = Stopwatch.GetTimestamp();
        var filter = ParseDocument(query.FilterJson, "filtro");
        var options = new FindOptions<BsonDocument>
        {
            Limit = query.Limit,
            Skip = query.Skip,
            MaxTime = query.MaxTimeMs is null ? null : TimeSpan.FromMilliseconds(query.MaxTimeMs.Value),
            Projection = ParseOptionalDocument(query.ProjectionJson, "projeção"),
            Sort = ParseOptionalDocument(query.SortJson, "ordenação"),
            Hint = ParseOptionalDocument(query.HintJson, "hint"),
            Comment = string.IsNullOrWhiteSpace(query.Comment) ? null : query.Comment.Trim(),
            BatchSize = Math.Min(query.BatchSize ?? 100, query.Limit),
            Collation = ParseOptionalCollation(query.CollationJson)
        };
        var collection = CreateClient(profile)
            .GetDatabase(query.Database)
            .GetCollection<BsonDocument>(query.Collection);
        using var cursor = await collection.FindAsync(filter, options, cancellationToken).ConfigureAwait(false);
        var documents = new List<string>(query.Limit);

        var characters = 0L;
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var document in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var json = document.ToJson(CanonicalJsonSettings);
                characters += json.Length;
                if (characters > 8_000_000) throw new InvalidOperationException("A página excede 8 milhões de caracteres. Reduza limit ou use projeção para excluir campos grandes.");
                documents.Add(json);
            }
        }

        return new QueryPage(documents, Stopwatch.GetElapsedTime(startedAt), documents.Count == query.Limit);
    }

    public async Task<CollectionCountResult> CountDocumentsAsync(ConnectionProfile profile, CollectionCountRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        request.Validate();
        var startedAt = Stopwatch.GetTimestamp();
        var collection = CreateClient(profile)
            .GetDatabase(request.Database)
            .GetCollection<BsonDocument>(request.Collection);
        TimeSpan? maxTime = request.MaxTimeMs is null ? null : TimeSpan.FromMilliseconds(request.MaxTimeMs.Value);
        var count = request.UseEstimatedCount
            ? await collection.EstimatedDocumentCountAsync(new EstimatedDocumentCountOptions { MaxTime = maxTime }, cancellationToken).ConfigureAwait(false)
            : await collection.CountDocumentsAsync(ParseDocument(request.FilterJson, "filtro"), new CountOptions { MaxTime = maxTime }, cancellationToken).ConfigureAwait(false);

        return new CollectionCountResult(count, request.UseEstimatedCount, Stopwatch.GetElapsedTime(startedAt));
    }

    public async Task<DistinctValuesResult> GetDistinctValuesAsync(ConnectionProfile profile, DistinctValuesRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        request.Validate();
        var startedAt = Stopwatch.GetTimestamp();
        var stages = new BsonDocument[]
        {
            new("$match", ParseDocument(request.FilterJson, "filtro")),
            new("$group", new BsonDocument("_id", "$" + request.Field)),
            new("$limit", request.MaximumValues + 1)
        };
        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);
        TimeSpan? maxTime = request.MaxTimeMs is null ? null : TimeSpan.FromMilliseconds(request.MaxTimeMs.Value);
        using var cursor = await GetCollection(profile, request.Database, request.Collection)
            .AggregateAsync(pipeline, new AggregateOptions { MaxTime = maxTime }, cancellationToken)
            .ConfigureAwait(false);
        var values = new List<string>(request.MaximumValues + 1);

        while (values.Count <= request.MaximumValues && await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var document in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                values.Add(document["_id"].ToJson(CanonicalJsonSettings));
                if (values.Count > request.MaximumValues)
                {
                    break;
                }
            }
        }

        var isTruncated = values.Count > request.MaximumValues;
        if (isTruncated)
        {
            values.RemoveAt(values.Count - 1);
        }

        return new DistinctValuesResult(values, isTruncated, Stopwatch.GetElapsedTime(startedAt));
    }

    public async Task<string> ExplainAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        query.Validate();
        var commandBody = new BsonDocument
        {
            ["find"] = query.Collection,
            ["filter"] = ParseDocument(query.FilterJson, "filtro"),
            ["limit"] = query.Limit,
            ["skip"] = query.Skip
        };

        var projection = ParseOptionalDocument(query.ProjectionJson, "projeção");
        if (projection is not null)
        {
            commandBody["projection"] = projection;
        }

        var sort = ParseOptionalDocument(query.SortJson, "ordenação");
        if (sort is not null)
        {
            commandBody["sort"] = sort;
        }

        var hint = ParseOptionalDocument(query.HintJson, "hint");
        if (hint is not null)
        {
            commandBody["hint"] = hint;
        }

        if (query.MaxTimeMs is not null)
        {
            commandBody["maxTimeMS"] = query.MaxTimeMs.Value;
        }
        if (!string.IsNullOrWhiteSpace(query.Comment))
        {
            commandBody["comment"] = query.Comment.Trim();
        }
        if (query.BatchSize is not null)
        {
            commandBody["batchSize"] = query.BatchSize.Value;
        }
        var collation = ParseOptionalDocument(query.CollationJson, "collation");
        if (collation is not null)
        {
            commandBody["collation"] = collation;
        }

        var explain = new BsonDocument
        {
            ["explain"] = commandBody,
            ["verbosity"] = "executionStats"
        };
        var result = await CreateClient(profile)
            .GetDatabase(query.Database)
            .RunCommandAsync<BsonDocument>(explain, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.ToJson(CanonicalJsonSettings);
    }

    public async Task<QueryPage> AggregateAsync(ConnectionProfile profile, AggregationQuery query, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        query.Validate();
        var stages = ParsePipeline(query.PipelineJson);
        var startedAt = Stopwatch.GetTimestamp();
        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);
        using var cursor = await GetCollection(profile, query.Database, query.Collection)
            .AggregateAsync(pipeline, new AggregateOptions { BatchSize = Math.Min(query.Limit, 100), MaxTime = TimeSpan.FromMinutes(5) }, cancellationToken)
            .ConfigureAwait(false);
        var documents = new List<string>(Math.Min(query.Limit, 1_000));

        var characters = 0L;
        while (documents.Count < query.Limit && await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var document in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (documents.Count == query.Limit)
                {
                    break;
                }

                var json = document.ToJson(CanonicalJsonSettings);
                characters += json.Length;
                if (characters > 8_000_000) throw new InvalidOperationException("A página excede 8 milhões de caracteres. Reduza limit ou use projeção para excluir campos grandes.");
                documents.Add(json);
            }
        }

        return new QueryPage(documents, Stopwatch.GetElapsedTime(startedAt), documents.Count == query.Limit);
    }

    public async Task<DatabaseExportResult> ExportDatabaseAsync(ConnectionProfile profile, DatabaseExportRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        request.Validate();
        var exportDirectory = CreateExportDirectory(request.Database);
        var database = CreateClient(profile).GetDatabase(request.Database);
        var names = await GetCollectionNamesAsync(profile, request.Database, cancellationToken).ConfigureAwait(false);
        var collections = new List<ExportCollection>(names.Count);
        long totalDocuments = 0;
        var isTruncated = false;
        var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson, Indent = true };

        for (var index = 0; index < names.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collectionName = names[index];
            var fileName = $"collection-{index + 1:D3}.extended.json";
            var filePath = Path.Combine(exportDirectory, fileName);
            var collection = database.GetCollection<BsonDocument>(collectionName);
            var count = 0;
            var collectionTruncated = false;

            await using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            using (var cursor = await collection.FindAsync(FilterDefinition<BsonDocument>.Empty, new FindOptions<BsonDocument> { Limit = request.DocumentsPerCollectionLimit + 1 }, cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteLineAsync("[").ConfigureAwait(false);
                var first = true;

                while (!collectionTruncated && await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var document in cursor.Current)
                    {
                        if (count == request.DocumentsPerCollectionLimit)
                        {
                            collectionTruncated = true;
                            break;
                        }

                        if (!first)
                        {
                            await writer.WriteLineAsync(",").ConfigureAwait(false);
                        }

                        await writer.WriteAsync(document.ToJson(settings)).ConfigureAwait(false);
                        first = false;
                        count++;
                    }
                }

                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync("]").ConfigureAwait(false);
            }

            collections.Add(new ExportCollection(collectionName, fileName, count, collectionTruncated));
            totalDocuments += count;
            isTruncated |= collectionTruncated;
        }

        var manifest = new ExportManifest(1, request.Database, DateTimeOffset.UtcNow, request.DocumentsPerCollectionLimit, collections);
        var manifestPath = Path.Combine(exportDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);

        return new DatabaseExportResult(exportDirectory, collections.Count, totalDocuments, isTruncated);
    }

    public async Task<DatabaseImportResult> ImportDatabaseAsync(ConnectionProfile profile, DatabaseImportRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var sourceDirectory = Path.GetFullPath(request.SourceDirectory);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"A pasta de origem não existe: {sourceDirectory}");
        }

        var manifestPath = Path.Combine(sourceDirectory, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("O manifesto da exportação não foi encontrado.", manifestPath);
        }

        var manifest = await ReadExportManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var targetDatabase = CreateClient(profile).GetDatabase(request.TargetDatabase);
        long totalDocuments = 0;

        foreach (var sourceCollection in manifest.Collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = GetSafeExportFilePath(sourceDirectory, sourceCollection.File);
            var documents = await ReadExportDocumentsAsync(sourceFile, cancellationToken).ConfigureAwait(false);

            if (documents.Count != sourceCollection.Documents)
            {
                throw new ArgumentException(
                    $"O arquivo {Path.GetFileName(sourceFile)} contém {documents.Count} documento(s), mas o manifesto declara {sourceCollection.Documents}.",
                    nameof(request));
            }

            if (documents.Count == 0)
            {
                continue;
            }

            var collection = targetDatabase.GetCollection<BsonDocument>(sourceCollection.Name);
            foreach (var batch in documents.Chunk(500))
            {
                var writes = batch.Select(CreateUpsertModel).ToArray();
                await collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = true }, cancellationToken).ConfigureAwait(false);
            }

            totalDocuments += documents.Count;
        }

        return new DatabaseImportResult(sourceDirectory, request.TargetDatabase, manifest.Collections.Count, totalDocuments);
    }

    public async Task<DocumentMutationResult> InsertAsync(ConnectionProfile profile, string database, string collection, string documentJson, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        var document = ParseDocument(documentJson, "documento");
        var target = GetCollection(profile, database, collection);
        await target.InsertOneAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new DocumentMutationResult(1, 1, document.TryGetValue("_id", out var id) ? id.ToJson(CanonicalJsonSettings) : null);
    }

    public async Task<long> InsertManyAsync(ConnectionProfile profile, BulkInsertRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var array = ParseArray(request.DocumentsJson, "array de documentos");

        if (array.Count is < 1 or > 10_000 || array.Count > request.MaximumDocuments)
        {
            throw new ArgumentOutOfRangeException(nameof(request), $"O lote contém {array.Count} documentos; o limite configurado é {request.MaximumDocuments}.");
        }

        if (array.Any(value => !value.IsBsonDocument))
        {
            throw new ArgumentException("Cada item do lote precisa ser um documento BSON.", nameof(request));
        }

        var documents = array.Select(value => value.AsBsonDocument).ToArray();
        await GetCollection(profile, request.Database, request.Collection)
            .InsertManyAsync(documents, new InsertManyOptions { IsOrdered = request.Ordered }, cancellationToken)
            .ConfigureAwait(false);
        return documents.LongLength;
    }

    public async Task<DocumentMutationResult> ReplaceAsync(ConnectionProfile profile, string database, string collection, string filterJson, string documentJson, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        var filter = ParseNonEmptyFilter(filterJson);
        var document = ParseDocument(documentJson, "documento");
        var result = await GetCollection(profile, database, collection)
            .ReplaceOneAsync(filter, document, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new DocumentMutationResult(result.MatchedCount, result.ModifiedCount);
    }

    public async Task<DocumentMutationResult> UpdateAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var filter = ParseNonEmptyFilter(request.FilterJson);
        var options = new UpdateOptions
        {
            IsUpsert = request.Upsert,
            ArrayFilters = string.IsNullOrWhiteSpace(request.ArrayFiltersJson)
                ? null
                : ParsePipeline(request.ArrayFiltersJson)
                    .Select(arrayFilter => (ArrayFilterDefinition<BsonDocument>)new BsonDocumentArrayFilterDefinition<BsonDocument>(arrayFilter))
                    .ToArray()
        };
        var collection = GetCollection(profile, request.Database, request.Collection);
        UpdateResult result;
        if (request.UpdateJson.TrimStart().StartsWith('['))
        {
            var pipeline = ParsePipeline(request.UpdateJson);
            result = await collection
                .UpdateOneAsync(filter, new PipelineUpdateDefinition<BsonDocument>(pipeline), options, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var update = ParseDocument(request.UpdateJson, "atualização");
            result = await collection
                .UpdateOneAsync(filter, new BsonDocumentUpdateDefinition<BsonDocument>(update), options, cancellationToken)
                .ConfigureAwait(false);
        }

        return new DocumentMutationResult(result.MatchedCount, result.ModifiedCount, result.UpsertedId?.ToString());
    }

    public async Task<FindAndModifyResult> FindAndModifyAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var filter = ParseNonEmptyFilter(request.FilterJson);
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = request.Upsert,
            ReturnDocument = ReturnDocument.After,
            ArrayFilters = string.IsNullOrWhiteSpace(request.ArrayFiltersJson)
                ? null
                : ParsePipeline(request.ArrayFiltersJson)
                    .Select(value => (ArrayFilterDefinition<BsonDocument>)new BsonDocumentArrayFilterDefinition<BsonDocument>(value))
                    .ToArray()
        };
        var collection = GetCollection(profile, request.Database, request.Collection);
        BsonDocument? document;
        if (request.UpdateJson.TrimStart().StartsWith('['))
        {
            document = await collection.FindOneAndUpdateAsync(filter, new PipelineUpdateDefinition<BsonDocument>(ParsePipeline(request.UpdateJson)), options, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            document = await collection.FindOneAndUpdateAsync(filter, new BsonDocumentUpdateDefinition<BsonDocument>(ParseDocument(request.UpdateJson, "atualização")), options, cancellationToken).ConfigureAwait(false);
        }

        return new FindAndModifyResult(document?.ToJson(CanonicalJsonSettings));
    }

    public async Task<DocumentMutationResult> DeleteAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        var filter = ParseNonEmptyFilter(filterJson);
        var result = await GetCollection(profile, database, collection)
            .DeleteOneAsync(filter, cancellationToken)
            .ConfigureAwait(false);
        return new DocumentMutationResult(result.DeletedCount, result.DeletedCount);
    }

    public async Task<DocumentMutationResult> DeleteManyAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        var filter = ParseNonEmptyFilter(filterJson);
        var result = await GetCollection(profile, database, collection)
            .DeleteManyAsync(filter, cancellationToken)
            .ConfigureAwait(false);
        return new DocumentMutationResult(result.DeletedCount, result.DeletedCount);
    }

    public async Task<IReadOnlyList<string>> GetIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        using var cursor = await GetCollection(profile, database, collection).Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
        var indexes = new List<string>();

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            indexes.AddRange(cursor.Current.Select(index => index.ToJson(CanonicalJsonSettings)));
        }

        return indexes;
    }

    public async Task<IReadOnlyList<string>> GetIndexUsageStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create([new BsonDocument("$indexStats", new BsonDocument())]);
        using var cursor = await GetCollection(profile, database, collection).AggregateAsync(pipeline, cancellationToken: cancellationToken).ConfigureAwait(false);
        var statistics = new List<string>();
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            statistics.AddRange(cursor.Current.Select(item => item.ToJson(CanonicalJsonSettings)));
        }

        return statistics;
    }

    public async Task<string> CreateIndexAsync(ConnectionProfile profile, IndexCreateRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var keys = ParseDocument(request.KeysJson, "chaves do índice");

        if (keys.ElementCount == 0)
        {
            throw new ArgumentException("O índice precisa de pelo menos uma chave.", nameof(request));
        }

        var options = new CreateIndexOptions<BsonDocument>
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            Unique = request.IsUnique,
            Sparse = request.IsSparse,
            ExpireAfter = request.ExpireAfterSeconds is null ? null : TimeSpan.FromSeconds(request.ExpireAfterSeconds.Value),
            PartialFilterExpression = ParseOptionalDocument(request.PartialFilterJson, "filtro parcial"),
            Collation = ParseOptionalCollation(request.CollationJson),
            Hidden = request.IsHidden,
            WildcardProjection = ParseOptionalDocument(request.WildcardProjectionJson, "projeção wildcard")
        };
        var model = new CreateIndexModel<BsonDocument>(new BsonDocumentIndexKeysDefinition<BsonDocument>(keys), options);
        return await GetCollection(profile, request.Database, request.Collection)
            .Indexes.CreateOneAsync(model, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetIndexVisibilityAsync(ConnectionProfile profile, IndexVisibilityRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        var command = new BsonDocument
        {
            ["collMod"] = request.Collection,
            ["index"] = new BsonDocument
            {
                ["name"] = request.Name.Trim(),
                ["hidden"] = request.IsHidden
            }
        };
        await CreateClient(profile)
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DropIndexAsync(ConnectionProfile profile, IndexDropRequest request, CancellationToken cancellationToken = default)
    {
        _operation.Value = new OperationEnvironment(_environments, _secrets, profile.Id);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _operation.Value.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        request.Validate();
        await GetCollection(profile, request.Database, request.Collection)
            .Indexes.DropOneAsync(request.Name.Trim(), cancellationToken)
            .ConfigureAwait(false);
    }

    private MongoClient CreateClient(ConnectionProfile profile, OperationEnvironment? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.ConnectionString);
        return _clients.Get(MongoClientSettings.FromConnectionString((environment ?? _operation.Value!).ResolvedConnection));
    }

    private IMongoCollection<BsonDocument> GetCollection(ConnectionProfile profile, string database, string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        return CreateClient(profile).GetDatabase(database).GetCollection<BsonDocument>(collection);
    }

    private static void EnsureUserCollection(string database, string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        if (string.IsNullOrWhiteSpace(collection) || collection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A coleção informada não pode ser administrada pela interface.", nameof(collection));
        }
    }

    private static CollectionValidationLevel ParseValidationLevel(string value) =>
        value.ToLowerInvariant() switch
        {
            "off" => CollectionValidationLevel.Off,
            "strict" => CollectionValidationLevel.Strict,
            "moderate" => CollectionValidationLevel.Moderate,
            _ => throw new InvalidOperationException($"O servidor retornou validationLevel não suportado: {value}.")
        };

    private static CollectionValidationAction ParseValidationAction(string value) =>
        value.ToLowerInvariant() switch
        {
            "error" => CollectionValidationAction.Error,
            "warn" => CollectionValidationAction.Warn,
            _ => throw new InvalidOperationException($"O servidor retornou validationAction não suportado: {value}.")
        };

    // UUID constructors become canonical binaries first; ENV values are then inserted as literal strings.
    private string ResolveDynamicJson(string json) => DynamicValues.ResolveJson(IdentifierRepresentationService.RewriteConstructors(json), key => _operation.Value!.Get(key));

    private BsonDocument ParseNonEmptyFilter(string filterJson)
    {
        var filter = ParseDocument(filterJson, "filtro");

        if (filter.ElementCount == 0)
        {
            throw new ArgumentException("Uma operação de alteração exige um filtro não vazio.", nameof(filterJson));
        }

        return filter;
    }

    private BsonDocument[] ParsePipeline(string pipelineJson)
    {
        try
        {
            var array = BsonSerializer.Deserialize<BsonArray>(ResolveDynamicJson(pipelineJson));

            if (array.Any(value => !value.IsBsonDocument))
            {
                throw new ArgumentException("Cada estágio do pipeline precisa ser um documento BSON.", nameof(pipelineJson));
            }

            return array.Select(value => value.AsBsonDocument).ToArray();
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O pipeline não contém Extended JSON válido: {exception.Message}", nameof(pipelineJson), exception);
        }
    }

    private BsonDocument ParseDocument(string json, string component)
    {
        try
        {
            return BsonDocument.Parse(ResolveDynamicJson(string.IsNullOrWhiteSpace(json) ? "{}" : json));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O {component} não contém JSON BSON válido: {exception.Message}", component, exception);
        }
    }

    private BsonArray ParseArray(string json, string component)
    {
        try
        {
            return BsonSerializer.Deserialize<BsonArray>(ResolveDynamicJson(json));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O {component} não contém um array Extended JSON válido: {exception.Message}", nameof(json), exception);
        }
    }

    private BsonDocument? ParseOptionalDocument(string? json, string component) =>
        string.IsNullOrWhiteSpace(json) ? null : ParseDocument(json, component);

    private Collation? ParseOptionalCollation(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : Collation.FromBsonDocument(ParseDocument(json, "collation"));

    private static ReplaceOneModel<BsonDocument> CreateUpsertModel(BsonDocument document)
    {
        if (!document.TryGetValue("_id", out var id))
        {
            throw new ArgumentException("Todo documento importado precisa conter _id para permitir upsert seguro.", nameof(document));
        }

        return new ReplaceOneModel<BsonDocument>(new BsonDocument("_id", id), document) { IsUpsert = true };
    }

    private static async Task<ExportManifest> ReadExportManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<ExportManifest>(json, ManifestJsonOptions)
            ?? throw new ArgumentException("O manifesto da exportação está vazio ou inválido.", nameof(manifestPath));

        if (manifest.FormatVersion != 1 || string.IsNullOrWhiteSpace(manifest.Database) || manifest.Collections is null)
        {
            throw new ArgumentException("O manifesto não pertence a um formato de exportação compatível.", nameof(manifestPath));
        }

        if (manifest.DocumentsPerCollectionLimit is < 1 or > 1_000_000
            || manifest.Collections.Any(collection => string.IsNullOrWhiteSpace(collection.Name)
                || string.IsNullOrWhiteSpace(collection.File)
                || collection.Documents < 0))
        {
            throw new ArgumentException("O manifesto contém uma coleção inválida.", nameof(manifestPath));
        }

        if (manifest.Collections.GroupBy(collection => collection.Name, StringComparer.Ordinal).Any(group => group.Count() > 1)
            || manifest.Collections.GroupBy(collection => collection.File, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("O manifesto contém nomes de coleção ou arquivos duplicados.", nameof(manifestPath));
        }

        return manifest;
    }

    internal static async Task<IReadOnlyList<BsonDocument>> ReadExportDocumentsAsync(string sourceFile, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            var array = BsonSerializer.Deserialize<BsonArray>(IdentifierRepresentationService.RewriteConstructors(json));

            if (array.Any(value => !value.IsBsonDocument))
            {
                throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} contém um item que não é documento BSON.", nameof(sourceFile));
            }

            return array.Select(value => value.AsBsonDocument).ToArray();
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} não contém Extended JSON válido: {exception.Message}", nameof(sourceFile), exception);
        }
    }

    private static string GetSafeExportFilePath(string sourceDirectory, string manifestFile)
    {
        var fileName = Path.GetFileName(manifestFile);

        if (!string.Equals(fileName, manifestFile, StringComparison.Ordinal) || !fileName.EndsWith(".extended.json", StringComparison.Ordinal))
        {
            throw new ArgumentException("O manifesto contém um caminho de arquivo de coleção inválido.", nameof(manifestFile));
        }

        var path = Path.GetFullPath(Path.Combine(sourceDirectory, fileName));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory)) + Path.DirectorySeparatorChar;

        if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
        {
            throw new FileNotFoundException("O arquivo de coleção declarado no manifesto não existe.", path);
        }

        return path;
    }

    private static string CreateExportDirectory(string database)
    {
        var root = LocalWorkspacePaths.GetExportsDirectory();
        Directory.CreateDirectory(root);
        var safeDatabase = string.Concat(database.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var name = $"{safeDatabase}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record ExportCollection(string Name, string File, int Documents, bool IsTruncated);

    private sealed record ExportManifest(
        int FormatVersion,
        string Database,
        DateTimeOffset CreatedAtUtc,
        int DocumentsPerCollectionLimit,
        IReadOnlyList<ExportCollection> Collections);
}
