using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LiteDbConnectionProfileRepositoryTests
{
    [Test]
    public async Task SaveAsyncRoundTripsProfileAndOrdersByName()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var zulu = ConnectionProfile.Create("Zulu", "mongodb://zulu:27017", "zulu");
        var alpha = ConnectionProfile.Create("Alpha", "mongodb://alpha:27017", "alpha");

        await repository.SaveAsync(zulu);
        await repository.SaveAsync(alpha);
        var profiles = await repository.GetAllAsync();

        Assert.That(profiles, Is.EqualTo(new[] { alpha, zulu }));
    }

    [Test]
    public async Task DeleteAsyncRemovesOnlyRequestedProfile()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var first = ConnectionProfile.Create("Primeira", "mongodb://first:27017");
        var second = ConnectionProfile.Create("Segunda", "mongodb://second:27017");

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);
        await repository.DeleteAsync(first.Id);
        var profiles = await repository.GetAllAsync();

        Assert.That(profiles, Is.EqualTo(new[] { second }));
    }

    [Test]
    public async Task GetAllAsyncPlacesFavoriteProfilesBeforeAlphabeticalProfiles()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var favorite = ConnectionProfile.Create("Zulu", "mongodb://zulu:27017", isFavorite: true);
        var regular = ConnectionProfile.Create("Alpha", "mongodb://alpha:27017");

        await repository.SaveAsync(regular);
        await repository.SaveAsync(favorite);
        var profiles = await repository.GetAllAsync();

        Assert.That(profiles, Is.EqualTo(new[] { favorite, regular }));
    }

    [Test]
    public async Task SaveAsyncRoundTripsFoldersAndOrdersThemWithinTheSameFavoriteState()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var production = ConnectionProfile.Create("Zulu", "mongodb://zulu:27017", folder: "Cliente B / Produção");
        var development = ConnectionProfile.Create("Alpha", "mongodb://alpha:27017", folder: "Cliente A / Desenvolvimento");

        await repository.SaveAsync(production);
        await repository.SaveAsync(development);
        var profiles = await repository.GetAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(profiles, Is.EqualTo(new[] { development, production }));
            Assert.That(profiles[0].Folder, Is.EqualTo("Cliente A / Desenvolvimento"));
        });
    }

    [Test]
    public async Task SaveAsyncRoundTripsLastConnectedTimestamp()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var occurredAt = new DateTimeOffset(2026, 9, 8, 12, 30, 0, TimeSpan.Zero);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017").MarkConnected(occurredAt);

        await repository.SaveAsync(profile);
        var loaded = await repository.GetAllAsync();

        Assert.That(loaded, Has.One.Matches<ConnectionProfile>(item =>
            item.Id == profile.Id && item.LastConnectedAt == occurredAt));
    }

    [Test]
    public async Task QueryHistoryRoundTripsAndFiltersByProfile()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Primeira", "mongodb://first:27017");
        var otherProfile = ConnectionProfile.Create("Segunda", "mongodb://second:27017");
        var firstEntry = QueryHistoryEntry.Create(profile.Id, new MongoQuery("catalogo", "clientes"), DateTimeOffset.UtcNow.AddMinutes(-1));
        var secondEntry = QueryHistoryEntry.Create(profile.Id, new MongoQuery("catalogo", "pedidos"), DateTimeOffset.UtcNow);
        var otherEntry = QueryHistoryEntry.Create(otherProfile.Id, new MongoQuery("catalogo", "auditoria"), DateTimeOffset.UtcNow.AddMinutes(1));

        await repository.SaveAsync(firstEntry);
        await repository.SaveAsync(secondEntry);
        await repository.SaveAsync(otherEntry);
        var entries = await repository.GetRecentAsync(profile.Id);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Collection, Is.EqualTo("pedidos"));
            Assert.That(entries[1].Collection, Is.EqualTo("clientes"));
            Assert.That(entries.All(entry => entry.ProfileId == profile.Id), Is.True);
        });
    }

    [Test]
    public async Task ScriptHistoryKeepsOnlyMostRecentEntryForPath()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var scriptPath = Path.Combine(fixture.DirectoryPath, "consulta.js");
        var first = ScriptHistoryEntry.Create(scriptPath, DateTimeOffset.UtcNow.AddMinutes(-1));
        var latest = ScriptHistoryEntry.Create(scriptPath, DateTimeOffset.UtcNow);

        await repository.SaveAsync(first);
        await repository.SaveAsync(latest);
        var entries = await repository.GetRecentAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Path, Is.EqualTo(Path.GetFullPath(scriptPath)));
            Assert.That(entries[0].LastAccessedAt, Is.EqualTo(latest.LastAccessedAt));
        });
    }

    [Test]
    public async Task ScriptHistoryRoundTripsOptInJsonInput()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var scriptPath = Path.Combine(fixture.DirectoryPath, "consulta.js");
        var entry = ScriptHistoryEntry.Create(scriptPath, DateTimeOffset.UtcNow, "{ \"region\": \"br\" }");

        await repository.SaveAsync(entry);

        var loaded = await repository.GetRecentAsync();

        Assert.That(loaded.Single().InputJson, Is.EqualTo(entry.InputJson));
    }

    [Test]
    public async Task SavedQueriesRoundTripForTheSelectedProfileAndOrderFavoritesFirst()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");
        var otherProfile = ConnectionProfile.Create("Secundária", "mongodb://secondary:27017");
        var regular = SavedQuery.Create("Clientes", profile.Id, new MongoQuery("catalogo", "clientes"));
        var favorite = SavedQuery.Create("Pedidos", profile.Id, new MongoQuery("catalogo", "pedidos"), isFavorite: true);
        var other = SavedQuery.Create("Auditoria", otherProfile.Id, new MongoQuery("catalogo", "auditoria"), isFavorite: true);

        await repository.SaveAsync(regular);
        await repository.SaveAsync(favorite);
        await repository.SaveAsync(other);
        var entries = await repository.GetAllAsync(profile.Id);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0], Is.EqualTo(favorite));
            Assert.That(entries[1], Is.EqualTo(regular));
        });
    }

    [Test]
    public async Task AuditEntriesRoundTripInReverseChronologicalOrder()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var first = AuditEntry.Create("collection.create", null, "catalogo", "clientes", "Coleção criada.", DateTimeOffset.UtcNow.AddMinutes(-1));
        var latest = AuditEntry.Create("collection.drop", null, "catalogo", "clientes", "Coleção removida.", DateTimeOffset.UtcNow);

        await repository.SaveAsync(first);
        await repository.SaveAsync(latest);
        var entries = await repository.GetRecentAuditAsync();

        Assert.That(entries, Is.EqualTo(new[] { latest, first }));
    }

    [Test]
    public async Task AuditEntriesRetainOnlyTheFiveHundredMostRecentItems()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var start = DateTimeOffset.UtcNow.AddMinutes(-501);

        for (var index = 0; index < 501; index++)
        {
            await repository.SaveAsync(AuditEntry.Create(
                "diagnostic",
                null,
                "catalogo",
                null,
                $"Evento {index}.",
                start.AddMinutes(index)));
        }

        var entries = await repository.GetRecentAuditAsync(500);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(500));
            Assert.That(entries[0].Summary, Is.EqualTo("Evento 500."));
            Assert.That(entries[^1].Summary, Is.EqualTo("Evento 1."));
        });
    }

    private sealed class TemporaryWorkspace : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "SlopStudio.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace()
        {
            Directory.CreateDirectory(_directory);
        }

        public string DatabasePath => Path.Combine(_directory, "workspace.db");

        public string DirectoryPath => _directory;

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
