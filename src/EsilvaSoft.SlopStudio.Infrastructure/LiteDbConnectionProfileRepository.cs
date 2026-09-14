using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed class LiteDbConnectionProfileRepository : IConnectionProfileRepository, IQueryHistoryRepository, IScriptHistoryRepository, ISavedQueryRepository, IAuditRepository, IWorkspaceSessionRepository, IEnvironmentVaultRepository, IConsoleHistoryRepository, IDisposable
{
    public Task SaveConsoleHistoryAsync(ConsoleHistoryEntry entry, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        if (!IsValidExecutionHistory(entry)) throw new ArgumentException("Histórico de execução inválido.");
        _database.GetCollection("consoleHistory").Upsert(new BsonDocument {
            ["_id"] = entry.Id, ["time"] = entry.ExecutedAt.UtcTicks,
            ["json"] = System.Text.Json.JsonSerializer.Serialize(entry)
        });
        var old = _database.GetCollection("consoleHistory").Query().OrderByDescending("time").Offset(500).ToArray();
        foreach (var item in old) _database.GetCollection("consoleHistory").Delete(item["_id"]);
    }, cancellationToken);

    public Task<IReadOnlyList<ConsoleHistoryEntry>> GetConsoleHistoryAsync(int maximum = 100, CancellationToken cancellationToken = default) => RunAsync<IReadOnlyList<ConsoleHistoryEntry>>(() =>
        _database.GetCollection("consoleHistory").Query().OrderByDescending("time").Limit(Math.Clamp(maximum, 1, 500)).ToArray()
            .Select(document => {
                var entry = System.Text.Json.JsonSerializer.Deserialize<ConsoleHistoryEntry>(document["json"].AsString);
                return entry is not null && IsValidExecutionHistory(entry) ? entry : throw new InvalidDataException("Histórico de execução ilegível ou de versão não suportada.");
            }).ToArray(), cancellationToken);

    private static bool IsValidExecutionHistory(ConsoleHistoryEntry entry) => entry.Version == 1 && entry.Id != Guid.Empty &&
        entry.Mode is "Console" or "Agregação" && entry.Collection is not null && entry.DocumentLimit is >= 1 and <= 10000;

    private const string CollectionName = "connectionProfiles";
    private const string QueryHistoryCollectionName = "queryHistory";
    private const string ScriptHistoryCollectionName = "scriptHistory";
    private const string SavedQueriesCollectionName = "savedQueries";
    private const string AuditCollectionName = "auditEvents";
    private readonly LiteDatabase _database;
    private readonly object _gate = new();
    private bool _disposed;
    private string? _environmentJson;
    private Exception? _environmentReadError;

    public EnvironmentVault LoadEnvironments()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_environmentReadError is { } error) throw new InvalidDataException("Cofre de ambientes ilegível.", error);
        var json = Volatile.Read(ref _environmentJson);
        if (json is null) return EnvironmentVault.CreateDefault();
        // Return an independent form/snapshot; editing a dialog must not change the active environment.
        var vault = System.Text.Json.JsonSerializer.Deserialize<EnvironmentVault>(json)
            ?? throw new InvalidDataException("Cofre de ambientes ilegível.");
        vault.Validate();
        return vault;
    }

    public void SaveEnvironments(EnvironmentVault vault)
    {
        vault.Validate();
        var json = System.Text.Json.JsonSerializer.Serialize(vault);
        lock (_gate)
        {
            _ = LoadEnvironments(); // Do not overwrite unreadable/newer data.
            _database.GetCollection("environmentVault").Upsert(new BsonDocument
            {
                ["_id"] = "current", ["json"] = json
            });
            Volatile.Write(ref _environmentJson, json);
        }
    }

    public Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default) => RunAsync(ReadSession, cancellationToken);

    private WorkspaceSession ReadSession()
    {
        var document = _database.GetCollection("workspaceSession").FindById("current");
        if (document is null) return new WorkspaceSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<WorkspaceSession>(document["json"].AsString)
            ?? throw new InvalidDataException("Sessão local inválida.");
        if (session.Version != 1) throw new InvalidDataException("Versão da sessão local não suportada.");
        if (session.Preferences?.Autocomplete is not { } autocomplete) throw new InvalidDataException("Preferências locais inválidas.");
        autocomplete.Validate();
        session.Preferences.ValidateUuid();
        return session;
    }

    public Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Version != 1) throw new ArgumentException("Versão da sessão local não suportada.", nameof(session));
        session.Preferences.Autocomplete.Validate();
        session.Preferences.ValidateUuid();
        // Persist policy at the boundary as well as in the UI. No credentials or results in this DTO.
        var allowed = session.Preferences.RecoverDrafts
            ? session.Tabs.Where(tab => !tab.ContainsResultData && (tab.ProfileId is null || !session.Preferences.ExcludedProfileIds.Contains(tab.ProfileId.Value))).ToArray()
            : [];
        var filtered = session with { Tabs = allowed, ActiveTabId = allowed.Any(tab => tab.Id == session.ActiveTabId) ? session.ActiveTabId : null };
        return RunAsync(() =>
        {
            _ = ReadSession(); // Preserve corrupt or newer snapshots, including autocomplete configuration.
            _database.GetCollection("workspaceSession").Upsert(new BsonDocument
            {
                ["_id"] = "current",
                ["json"] = System.Text.Json.JsonSerializer.Serialize(filtered)
            });
        }, cancellationToken);
    }

    public LiteDbConnectionProfileRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var directory = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _database = new LiteDatabase($"Filename={databasePath};Connection=direct");
        try { _environmentJson = _database.GetCollection("environmentVault").FindById("current")?["json"].AsString; }
        catch (Exception ex) { _environmentReadError = ex; }
        lock (_gate)
        {
            var collection = _database.GetCollection<ConnectionProfileDocument>(CollectionName);
            collection.EnsureIndex(profile => profile.Name, unique: true);
            _database.GetCollection<QueryHistoryDocument>(QueryHistoryCollectionName)
                .EnsureIndex(entry => entry.ExecutedAtUtc);
            _database.GetCollection<ScriptHistoryDocument>(ScriptHistoryCollectionName)
                .EnsureIndex(entry => entry.LastAccessedUtcTicks);
            _database.GetCollection<SavedQueryDocument>(SavedQueriesCollectionName)
                .EnsureIndex(entry => entry.UpdatedAtUtcTicks);
            _database.GetCollection<AuditEntryDocument>(AuditCollectionName)
                .EnsureIndex(entry => entry.OccurredAtUtcTicks);
        }
    }

    public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var profiles = _database.GetCollection<ConnectionProfileDocument>(CollectionName)
                .FindAll()
                .OrderByDescending(profile => profile.IsFavorite)
                .ThenBy(profile => profile.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToDomain)
                .ToArray();

            return (IReadOnlyList<ConnectionProfile>)profiles;
        }, cancellationToken);

    public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var collection = _database.GetCollection<ConnectionProfileDocument>(CollectionName);
            collection.Upsert(FromDomain(profile));
        }, cancellationToken);

    public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        RunAsync(() => _database.GetCollection<ConnectionProfileDocument>(CollectionName).Delete(profileId), cancellationToken);

    public Task<IReadOnlyList<QueryHistoryEntry>> GetRecentAsync(Guid? profileId, int maximum = 50, CancellationToken cancellationToken = default)
    {
        if (maximum is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "O histórico deve conter entre 1 e 200 itens.");
        }

        return RunAsync(() =>
        {
            var query = _database.GetCollection<QueryHistoryDocument>(QueryHistoryCollectionName)
                .Query()
                .OrderByDescending(entry => entry.ExecutedAtUtc);
            if (profileId is not null)
            {
                query = query.Where(entry => entry.ProfileId == profileId);
            }

            return (IReadOnlyList<QueryHistoryEntry>)query
                .Limit(maximum)
                .ToEnumerable()
                .Select(ToDomain)
                .ToArray();
        }, cancellationToken);
    }

    public Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        entry.Validate();
        return RunAsync(() => _database.GetCollection<QueryHistoryDocument>(QueryHistoryCollectionName).Upsert(FromDomain(entry)), cancellationToken);
    }

    public Task<IReadOnlyList<ScriptHistoryEntry>> GetRecentAsync(int maximum = 50, CancellationToken cancellationToken = default)
    {
        if (maximum is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "O histórico deve conter entre 1 e 200 itens.");
        }

        return RunAsync(() =>
        {
            var entries = _database.GetCollection<ScriptHistoryDocument>(ScriptHistoryCollectionName)
                .Query()
                .OrderByDescending(entry => entry.LastAccessedUtcTicks)
                .Limit(maximum)
                .ToEnumerable()
                .Select(ToDomain)
                .ToArray();
            return (IReadOnlyList<ScriptHistoryEntry>)entries;
        }, cancellationToken);
    }

    public Task SaveAsync(ScriptHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        entry.Validate();
        return RunAsync(() =>
        {
            var collection = _database.GetCollection<ScriptHistoryDocument>(ScriptHistoryCollectionName);
            var existing = collection.FindOne(item => item.Path == entry.Path);
            var id = existing?.Id ?? entry.Id;
            collection.Upsert(FromDomain(entry) with { Id = id });
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SavedQuery>> GetAllAsync(Guid? profileId, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var queries = _database.GetCollection<SavedQueryDocument>(SavedQueriesCollectionName)
                .FindAll()
                .Where(query => query.ProfileId == profileId)
                .OrderByDescending(query => query.IsFavorite)
                .ThenBy(query => query.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToDomain)
                .ToArray();
            return (IReadOnlyList<SavedQuery>)queries;
        }, cancellationToken);

    public Task SaveAsync(SavedQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate();
        return RunAsync(() => _database.GetCollection<SavedQueryDocument>(SavedQueriesCollectionName).Upsert(FromDomain(query)), cancellationToken);
    }

    public Task DeleteSavedAsync(Guid id, CancellationToken cancellationToken = default) =>
        RunAsync(() => _database.GetCollection<SavedQueryDocument>(SavedQueriesCollectionName).Delete(id), cancellationToken);

    public Task<IReadOnlyList<AuditEntry>> GetRecentAuditAsync(int maximum = 100, CancellationToken cancellationToken = default)
    {
        if (maximum is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "A auditoria deve conter entre 1 e 500 itens.");
        }

        return RunAsync(() =>
        {
            var entries = _database.GetCollection<AuditEntryDocument>(AuditCollectionName)
                .Query()
                .OrderByDescending(entry => entry.OccurredAtUtcTicks)
                .Limit(maximum)
                .ToEnumerable()
                .Select(ToDomain)
                .ToArray();
            return (IReadOnlyList<AuditEntry>)entries;
        }, cancellationToken);
    }

    public Task SaveAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        entry.Validate();
        return RunAsync(() =>
        {
            var collection = _database.GetCollection<AuditEntryDocument>(AuditCollectionName);
            collection.Upsert(FromDomain(entry));
            var obsoleteIds = collection.Query()
                .OrderByDescending(item => item.OccurredAtUtcTicks)
                .Skip(500)
                .ToEnumerable()
                .Select(item => item.Id)
                .ToArray();
            foreach (var obsoleteId in obsoleteIds)
            {
                collection.Delete(obsoleteId);
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _database.Dispose();
            _disposed = true;
        }
    }

    private Task RunAsync(Action action, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ThrowIfDisposed();
                action();
            }
        }, cancellationToken);

    private Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ThrowIfDisposed();
                return action();
            }
        }, cancellationToken);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static ConnectionProfile ToDomain(ConnectionProfileDocument profile) =>
        new(
            profile.Id,
            profile.Name,
            profile.ConnectionString,
            profile.DefaultDatabase,
            profile.Environment,
            profile.Color,
            profile.Tags,
            profile.IsReadOnly,
            profile.IsFavorite,
            profile.LastConnectedAt,
            profile.Folder);

    private static ConnectionProfileDocument FromDomain(ConnectionProfile profile) =>
        new()
        {
            Id = profile.Id,
            Name = profile.Name,
            ConnectionString = profile.ConnectionString,
            DefaultDatabase = profile.DefaultDatabase,
            Environment = profile.Environment,
            Color = profile.Color,
            Tags = profile.Tags,
            IsReadOnly = profile.IsReadOnly,
            IsFavorite = profile.IsFavorite,
            LastConnectedAt = profile.LastConnectedAt,
            Folder = profile.Folder
        };

    private static QueryHistoryEntry ToDomain(QueryHistoryDocument entry) =>
        new(entry.Id, entry.ProfileId, entry.Database, entry.Collection, entry.FilterJson, entry.ProjectionJson, entry.SortJson, entry.HintJson, entry.Limit, entry.Skip, entry.MaxTimeMs, new DateTimeOffset(DateTime.SpecifyKind(entry.ExecutedAtUtc, DateTimeKind.Utc)), entry.Comment, entry.BatchSize, entry.CollationJson);

    private static QueryHistoryDocument FromDomain(QueryHistoryEntry entry) =>
        new()
        {
            Id = entry.Id,
            ProfileId = entry.ProfileId,
            Database = entry.Database,
            Collection = entry.Collection,
            FilterJson = entry.FilterJson,
            ProjectionJson = entry.ProjectionJson,
            SortJson = entry.SortJson,
            HintJson = entry.HintJson,
            Limit = entry.Limit,
            Skip = entry.Skip,
            MaxTimeMs = entry.MaxTimeMs,
            Comment = entry.Comment,
            BatchSize = entry.BatchSize,
            CollationJson = entry.CollationJson,
            ExecutedAtUtc = entry.ExecutedAt.UtcDateTime
        };

    private static ScriptHistoryEntry ToDomain(ScriptHistoryDocument entry) =>
        new(entry.Id, entry.Path, new DateTimeOffset(new DateTime(entry.LastAccessedUtcTicks, DateTimeKind.Utc)), entry.InputJson);

    private static ScriptHistoryDocument FromDomain(ScriptHistoryEntry entry) =>
        new()
        {
            Id = entry.Id,
            Path = entry.Path,
            LastAccessedUtcTicks = entry.LastAccessedAt.UtcDateTime.Ticks,
            InputJson = entry.InputJson
        };

    private static SavedQuery ToDomain(SavedQueryDocument query) =>
        new(query.Id, query.Name, query.ProfileId, query.Database, query.Collection, query.FilterJson, query.ProjectionJson, query.SortJson, query.HintJson, query.Limit, query.Skip, query.MaxTimeMs, query.IsFavorite, new DateTimeOffset(new DateTime(query.UpdatedAtUtcTicks, DateTimeKind.Utc)), query.Comment, query.BatchSize, query.CollationJson);

    private static SavedQueryDocument FromDomain(SavedQuery query) =>
        new()
        {
            Id = query.Id,
            Name = query.Name,
            ProfileId = query.ProfileId,
            Database = query.Database,
            Collection = query.Collection,
            FilterJson = query.FilterJson,
            ProjectionJson = query.ProjectionJson,
            SortJson = query.SortJson,
            HintJson = query.HintJson,
            Limit = query.Limit,
            Skip = query.Skip,
            MaxTimeMs = query.MaxTimeMs,
            Comment = query.Comment,
            BatchSize = query.BatchSize,
            CollationJson = query.CollationJson,
            IsFavorite = query.IsFavorite,
            UpdatedAtUtcTicks = query.UpdatedAt.UtcDateTime.Ticks
        };

    private static AuditEntry ToDomain(AuditEntryDocument entry) =>
        new(entry.Id, new DateTimeOffset(new DateTime(entry.OccurredAtUtcTicks, DateTimeKind.Utc)), entry.Action, entry.ProfileId, entry.Database, entry.Collection, entry.Summary);

    private static AuditEntryDocument FromDomain(AuditEntry entry) =>
        new()
        {
            Id = entry.Id,
            OccurredAtUtcTicks = entry.OccurredAt.UtcDateTime.Ticks,
            Action = entry.Action,
            ProfileId = entry.ProfileId,
            Database = entry.Database,
            Collection = entry.Collection,
            Summary = entry.Summary
        };

    private sealed class ConnectionProfileDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string ConnectionString { get; init; } = string.Empty;

        public string? DefaultDatabase { get; init; }

        public string? Environment { get; init; }

        public string? Color { get; init; }

        public string? Tags { get; init; }

        public bool IsReadOnly { get; init; }

        public bool IsFavorite { get; init; }

        public DateTimeOffset? LastConnectedAt { get; init; }

        public string? Folder { get; init; }
    }

    private sealed class QueryHistoryDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public Guid? ProfileId { get; init; }

        public string Database { get; init; } = string.Empty;

        public string Collection { get; init; } = string.Empty;

        public string FilterJson { get; init; } = "{}";

        public string? ProjectionJson { get; init; }

        public string? SortJson { get; init; }

        public string? HintJson { get; init; }

        public int Limit { get; init; }

        public int Skip { get; init; }

        public int? MaxTimeMs { get; init; }

        public string? Comment { get; init; }

        public int? BatchSize { get; init; }

        public string? CollationJson { get; init; }

        public DateTime ExecutedAtUtc { get; init; }
    }

    private sealed record ScriptHistoryDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public string Path { get; init; } = string.Empty;

        public long LastAccessedUtcTicks { get; init; }

        public string? InputJson { get; init; }
    }

    private sealed class SavedQueryDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public Guid? ProfileId { get; init; }

        public string Database { get; init; } = string.Empty;

        public string Collection { get; init; } = string.Empty;

        public string FilterJson { get; init; } = "{}";

        public string? ProjectionJson { get; init; }

        public string? SortJson { get; init; }

        public string? HintJson { get; init; }

        public int Limit { get; init; }

        public int Skip { get; init; }

        public int? MaxTimeMs { get; init; }

        public string? Comment { get; init; }

        public int? BatchSize { get; init; }

        public string? CollationJson { get; init; }

        public bool IsFavorite { get; init; }

        public long UpdatedAtUtcTicks { get; init; }
    }

    private sealed class AuditEntryDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public long OccurredAtUtcTicks { get; init; }

        public string Action { get; init; } = string.Empty;

        public Guid? ProfileId { get; init; }

        public string? Database { get; init; }

        public string? Collection { get; init; }

        public string Summary { get; init; } = string.Empty;
    }
}
