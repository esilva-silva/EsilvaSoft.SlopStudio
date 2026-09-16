using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string CollectionName = "connectionProfiles";

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
}
