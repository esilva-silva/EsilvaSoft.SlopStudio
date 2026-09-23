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
            var existing = collection.FindById(profile.Id);
            var sourceGenerationId = ResolveSourceGenerationId(existing, profile);
            collection.Upsert(FromDomain(profile with { SourceGenerationId = sourceGenerationId }));
        }, cancellationToken);

    /// <summary>
    /// Renews <see cref="ConnectionProfile.SourceGenerationId"/> only when the data origin actually changes
    /// (<c>ConnectionString</c>, <c>TargetHost</c> or <c>Environment</c> differ from the stored document), per
    /// DEC-L-GENERATION. A brand-new profile, or a legacy document persisted before this field existed, receives
    /// a fresh id without invalidating anything else; renaming, favoriting or any other cosmetic edit keeps the
    /// stored value unchanged.
    /// </summary>
    private static Guid ResolveSourceGenerationId(ConnectionProfileDocument? existing, ConnectionProfile incoming)
    {
        if (existing is null)
        {
            return Guid.NewGuid();
        }

        var originChanged = existing.ConnectionString != incoming.ConnectionString
            || existing.TargetHost != incoming.TargetHost
            || existing.Environment != incoming.Environment;

        if (originChanged)
        {
            return Guid.NewGuid();
        }

        return existing.SourceGenerationId ?? Guid.NewGuid();
    }

    /// <summary>
    /// Deletes the profile and, in the same short transaction, every namespace learned for it
    /// (DEC-L-RETENTION: profile removal is one of the events that cascades automatically, unlike disconnect or
    /// opt-out, which never delete). A pending L15 read that started before this commit still resolves against a
    /// snapshot read outside the transaction, per LiteDB's usual isolation; it simply becomes stale the moment this
    /// commits, exactly like any other concurrent read of a row being deleted.
    /// </summary>
    public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            EnsureLearnedSchemaIndexes();
            InTransaction(() =>
            {
                _database.GetCollection<ConnectionProfileDocument>(CollectionName).Delete(profileId);
                LearnedSchemaCollection().DeleteMany(Query.EQ("ProfileId", profileId));
            });
        }, cancellationToken);

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
            profile.Folder)
        {
            TargetHost = profile.TargetHost,
            SourceGenerationId = profile.SourceGenerationId,
            LocalAiContextEnabled = profile.LocalAiContextEnabled
        };

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
            Folder = profile.Folder,
            TargetHost = profile.TargetHost,
            SourceGenerationId = profile.SourceGenerationId,
            LocalAiContextEnabled = profile.LocalAiContextEnabled
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

        public string? TargetHost { get; init; }

        /// <summary>
        /// Additive field (DEC-L-GENERATION): absent on documents written before this field existed. Missing
        /// values deserialize as null and are treated as "needs a first assignment" by
        /// <see cref="ResolveSourceGenerationId"/>, never as an error and never as a reason to rewrite the
        /// rest of the document.
        /// </summary>
        public Guid? SourceGenerationId { get; init; }

        /// <summary>Additive per-connection local AI context opt-out; legacy profiles preserve the enabled default.</summary>
        public bool LocalAiContextEnabled { get; init; } = true;
    }
}
