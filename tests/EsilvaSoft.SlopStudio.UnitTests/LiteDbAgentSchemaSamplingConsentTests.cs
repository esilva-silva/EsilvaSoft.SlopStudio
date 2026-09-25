using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LiteDbAgentSchemaSamplingConsentTests
{
    private const string CollectionName = "agentSchemaSamplingConsents";

    [Test]
    public async Task MissingConsentDeniesAndDoesNotCreateAStoredDefault()
    {
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var consents = (IAgentSchemaSamplingConsentRepository)owner;
            Assert.That(await consents.LoadAsync(principalId, default), Is.Null);
            var provider = (IAgentSchemaSamplingConsentProvider)owner;
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, Guid.NewGuid(), Guid.NewGuid(), "db", "col"), default), Is.False);
        }
        using var raw = fixture.OpenOffline();
        Assert.That(raw.GetCollection(CollectionName).Count(), Is.Zero);
    }

    [Test]
    public async Task GrantedConsentAllowsExactCollectionAndDeniesOtherCollectionDatabaseOrConnection()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var grant = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "col"));
        var saved = await consents.SaveAsync(principalId, [grant], 0);
        Assert.That(saved.Revision, Is.EqualTo(1));

        Assert.Multiple(async () =>
        {
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "col"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "other"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "otherDb", "col"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, Guid.NewGuid(), generationId, "db", "col"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(Guid.NewGuid(), connectionId, generationId, "db", "col"), default), Is.False);
        });
    }

    [Test]
    public async Task ConnectionWideConsentCoversEveryDatabaseAndCollectionOfThatConnectionOnly()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        await consents.SaveAsync(principalId,
            [Consent(principalId, generationId, AgentNamespaceScope.ForConnection(connectionId))], 0);

        Assert.Multiple(async () =>
        {
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "any", "thing"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "other", "col"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, Guid.NewGuid(), generationId, "any", "thing"), default), Is.False);
        });
    }

    [Test]
    public async Task ChangedProfileGenerationDeniesAPreviouslyGrantedConsent()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var originalGeneration = Guid.NewGuid();
        await consents.SaveAsync(principalId,
            [Consent(principalId, originalGeneration, AgentNamespaceScope.ForCollection(connectionId, "db", "col"))], 0);

        Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, originalGeneration, "db", "col"), default), Is.True);
        var reconnectedGeneration = Guid.NewGuid();
        Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, reconnectedGeneration, "db", "col"), default), Is.False);
    }

    [Test]
    public async Task ExpiredConsentDeniesWhileUnexpiredAndNeverExpiringConsentsStillAllow()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expired = Consent(principalId, generationId,
            AgentNamespaceScope.ForCollection(connectionId, "db", "expired"), now.AddHours(-2), now.AddHours(-1));
        var stillValid = Consent(principalId, generationId,
            AgentNamespaceScope.ForCollection(connectionId, "db", "valid"), now.AddHours(-2), now.AddHours(1));
        var neverExpires = Consent(principalId, generationId,
            AgentNamespaceScope.ForCollection(connectionId, "db", "forever"), now.AddHours(-2), null);
        await consents.SaveAsync(principalId, [expired, stillValid, neverExpires], 0);

        Assert.Multiple(async () =>
        {
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "expired"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "valid"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "forever"), default), Is.True);
        });
    }

    [Test]
    public async Task RevocationRemovesTheEntryAndDeniesWithoutAffectingOtherConsents()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var revoked = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "revoked"));
        var kept = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "kept"));
        var saved = await consents.SaveAsync(principalId, [revoked, kept], 0);

        // A future UI performs revocation as: reload, drop the matching entry, save at the loaded revision.
        var afterRevocation = await consents.SaveAsync(principalId, [kept], saved.Revision);

        Assert.Multiple(async () =>
        {
            Assert.That(afterRevocation.Revision, Is.EqualTo(2));
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "revoked"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "kept"), default), Is.True);
        });
    }

    [Test]
    public async Task RoundTripPreservesConsentsAndOtherWorkspaceDataAcrossReopen()
    {
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var grant = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "catalogo", "pedidos"));
        var profile = ConnectionProfile.Create("local-only-name", "mongodb://user:credential-canary@host-canary:27017");
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, new InMemoryProfileSecretStore()))
        {
            await owner.SaveAsync(profile);
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(principalId, [grant], 0);
        }
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var loaded = await ((IAgentSchemaSamplingConsentRepository)owner).LoadAsync(principalId, default);
            Assert.That(loaded, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(loaded!.IsValid, Is.True);
                Assert.That(loaded.SchemaVersion, Is.EqualTo(1));
                Assert.That(loaded.Consents, Has.Count.EqualTo(1));
                Assert.That(loaded.Consents[0].Scope, Is.EqualTo(grant.Scope));
                Assert.That(loaded.Consents[0].SourceGenerationId, Is.EqualTo(generationId));
            });
            var persistedProfile = (await owner.GetAllAsync()).Single();
            Assert.Multiple(() =>
            {
                Assert.That(persistedProfile.ConnectionString, Is.EqualTo("mongodb://user@host-canary:27017"));
                Assert.That(persistedProfile.SecretReference, Is.Not.Null);
            });
        }
        using var raw = fixture.OpenOffline();
        var serialized = raw.GetCollection(CollectionName).FindById(principalId).ToString();
        Assert.That(serialized, Does.Not.Contain("credential-canary").And.Not.Contain("host-canary").And.Not.Contain("local-only-name"));
    }

    [Test]
    public async Task ConcurrentGrantAndRevokeHaveOneWinnerAndAConsistentFinalState()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var baseline = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "base"));
        await consents.SaveAsync(principalId, [baseline], 0);

        // Twelve concurrent writers race from the same observed revision: one grants an extra namespace, the rest
        // attempt to revoke everything. At most one of the 12 wins; every loser must reload before retrying.
        var writers = Enumerable.Range(0, 12).Select(async index =>
        {
            try
            {
                if (index == 0)
                {
                    var granted = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "extra"));
                    await consents.SaveAsync(principalId, [baseline, granted], 1);
                }
                else
                {
                    await consents.SaveAsync(principalId, [], 1);
                }
                return true;
            }
            catch (AgentSchemaSamplingConsentConcurrencyException)
            {
                return false;
            }
        });
        Assert.That((await Task.WhenAll(writers)).Count(won => won), Is.EqualTo(1));
        var final = await consents.LoadAsync(principalId, default);
        Assert.That(final!.Revision, Is.EqualTo(2));
        // Whichever writer won, the result is one specific, fully-applied outcome — never a merge of both.
        Assert.That(final.Consents.Count, Is.AnyOf(0, 2));
    }

    [TestCase("schema")]
    [TestCase("revision")]
    [TestCase("revisionType")]
    [TestCase("missingConsents")]
    [TestCase("extraField")]
    [TestCase("consentType")]
    [TestCase("duplicate")]
    [TestCase("principal")]
    [TestCase("missingNamespace")]
    [TestCase("collectionWithoutDatabase")]
    [TestCase("sourceGeneration")]
    [TestCase("expiryBeforeGrant")]
    public async Task CorruptConsentDeniesAndCannotBeOverwrittenEvenWithEmptyConsents(string corruption)
    {
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var grant = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForCollection(connectionId, "db", "col"));
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(principalId, [grant], 0);
        string corrupted;
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var doc = collection.FindById(principalId);
            Corrupt(doc, corruption);
            collection.Update(doc);
            corrupted = collection.FindById(principalId).ToString();
        }
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var consents = (IAgentSchemaSamplingConsentRepository)owner;
            var provider = (IAgentSchemaSamplingConsentProvider)owner;
            Assert.ThrowsAsync<InvalidDataException>(() => consents.LoadAsync(principalId, default));
            Assert.ThrowsAsync<InvalidDataException>(() =>
                provider.HasLocalConsentAsync(Request(principalId, connectionId, grant.SourceGenerationId, "db", "col"), default));
            Assert.ThrowsAsync<InvalidDataException>(() => consents.SaveAsync(principalId, [], 1));
        }
        using var verification = fixture.OpenOffline();
        Assert.That(verification.GetCollection(CollectionName).FindById(principalId).ToString(), Is.EqualTo(corrupted));
    }

    [Test]
    public async Task InvalidDuplicateOrCrossPrincipalReplacementPreservesValidConsent()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var principalId = Guid.NewGuid();
        var grant = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForCollection(Guid.NewGuid(), "db", "col"));
        await consents.SaveAsync(principalId, [grant], 0);
        Assert.Throws<ArgumentException>(() => consents.SaveAsync(principalId, [grant, grant], 1));
        Assert.Throws<ArgumentException>(() =>
            consents.SaveAsync(principalId, [Consent(Guid.NewGuid(), Guid.NewGuid(), AgentNamespaceScope.ForConnection(Guid.NewGuid()))], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => consents.SaveAsync(principalId, [], long.MaxValue));
        Assert.That((await consents.LoadAsync(principalId, default))!.Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task DeletingTheProfileRemovesOnlyItsOwnConnectionScopedConsents()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var deleted = ConnectionProfile.Create("deleted-profile", "mongodb://localhost:27017");
        var kept = ConnectionProfile.Create("kept-profile", "mongodb://localhost:27018");
        await owner.SaveAsync(deleted);
        await owner.SaveAsync(kept);
        var principalId = Guid.NewGuid();
        var otherPrincipalId = Guid.NewGuid();
        var forDeletedConnection = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForCollection(deleted.Id, "db", "col"));
        var forKeptConnection = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForCollection(kept.Id, "db", "col"));
        var otherPrincipalForDeletedConnection = Consent(otherPrincipalId, Guid.NewGuid(), AgentNamespaceScope.ForConnection(deleted.Id));
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        await consents.SaveAsync(principalId, [forDeletedConnection, forKeptConnection], 0);
        await consents.SaveAsync(otherPrincipalId, [otherPrincipalForDeletedConnection], 0);

        await owner.DeleteAsync(deleted.Id);

        var reloaded = await consents.LoadAsync(principalId, default);
        Assert.That(reloaded!.Consents.Select(item => item.Scope), Is.EqualTo(new[] { forKeptConnection.Scope }));
        var otherReloaded = await consents.LoadAsync(otherPrincipalId, default);
        Assert.That(otherReloaded!.Consents, Is.Empty);
    }

    [Test]
    public async Task DeletingAProfileLeavesAnUnrelatedCorruptConsentDocumentUntouched()
    {
        using var fixture = new Workspace();
        var deleted = ConnectionProfile.Create("deleted-profile", "mongodb://localhost:27017");
        var corruptPrincipalId = Guid.NewGuid();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            await owner.SaveAsync(deleted);
            var grant = Consent(corruptPrincipalId, Guid.NewGuid(), AgentNamespaceScope.ForConnection(deleted.Id));
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(corruptPrincipalId, [grant], 0);
        }
        string corrupted;
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var doc = collection.FindById(corruptPrincipalId);
            doc["schemaVersion"] = 2;
            collection.Update(doc);
            corrupted = collection.FindById(corruptPrincipalId).ToString();
        }
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await owner.DeleteAsync(deleted.Id);
        using var verification = fixture.OpenOffline();
        Assert.That(verification.GetCollection(CollectionName).FindById(corruptPrincipalId).ToString(), Is.EqualTo(corrupted));
    }

    [Test]
    public async Task CanceledSaveAndCallerMutationCannotChangeTheStoredConsent()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var principalId = Guid.NewGuid();
        var grant = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForConnection(Guid.NewGuid()));
        var mutable = new List<AgentSchemaSamplingConsentGrant> { grant };
        var save = consents.SaveAsync(principalId, mutable, 0);
        mutable.Clear();
        await save;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => consents.SaveAsync(principalId, [], 1, cancellation.Token));
        var loaded = await consents.LoadAsync(principalId, default);
        Assert.That(loaded!.Consents, Has.Count.EqualTo(1));
        Assert.That(loaded.Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task WriteFailureIsVisibleAndRecoveryDoesNotResetOtherPrincipalsRevision()
    {
        using var fixture = new Workspace();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstGrant = Consent(first, Guid.NewGuid(), AgentNamespaceScope.ForConnection(Guid.NewGuid()));
        var secondGrant = Consent(second, Guid.NewGuid(), AgentNamespaceScope.ForConnection(Guid.NewGuid()));
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(first, [firstGrant], 0);
        // Offline fault injection; never open another database while the workspace owner is alive.
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).EnsureIndex("revision", unique: true);
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var consents = (IAgentSchemaSamplingConsentRepository)owner;
            Assert.ThrowsAsync<LiteException>(() => consents.SaveAsync(second, [secondGrant], 0));
            Assert.That(await consents.LoadAsync(second, default), Is.Null);
            Assert.That((await consents.LoadAsync(first, default))!.Revision, Is.EqualTo(1));
        }
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).DropIndex("revision");
        using var recoveredOwner = new LiteDbConnectionProfileRepository(fixture.Path);
        var recovered = (IAgentSchemaSamplingConsentRepository)recoveredOwner;
        Assert.That((await recovered.SaveAsync(second, [secondGrant], 0)).Revision, Is.EqualTo(1));
        Assert.That((await recovered.SaveAsync(first, [], 1)).Revision, Is.EqualTo(2));
    }

    [Test]
    public async Task ClosedOwnerFailsVisiblyAndProviderDenies()
    {
        using var fixture = new Workspace();
        var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var grant = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "col"));
        await consents.SaveAsync(principalId, [grant], 0);
        owner.Dispose();
        Assert.ThrowsAsync<ObjectDisposedException>(() => consents.LoadAsync(principalId, default));
        Assert.ThrowsAsync<ObjectDisposedException>(() => consents.SaveAsync(principalId, [], 1));
        Assert.ThrowsAsync<ObjectDisposedException>(() =>
            provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "col"), default));
    }

    private static void Corrupt(BsonDocument doc, string corruption)
    {
        var consent = doc["consents"].AsArray[0].AsDocument;
        switch (corruption)
        {
            case "schema": doc["schemaVersion"] = 2; break;
            case "revision": doc["revision"] = 0L; break;
            case "revisionType": doc["revision"] = "1"; break;
            case "missingConsents": doc.Remove("consents"); break;
            case "extraField": doc["grantedByDefault"] = true; break;
            case "consentType": doc["consents"].AsArray[0] = BsonValue.Null; break;
            case "duplicate": doc["consents"].AsArray.Add(consent); break;
            case "principal": consent["principalId"] = Guid.NewGuid(); break;
            case "missingNamespace": consent.Remove("databaseName"); break;
            case "collectionWithoutDatabase": consent["databaseName"] = BsonValue.Null; break;
            case "sourceGeneration": consent["sourceGenerationId"] = Guid.Empty; break;
            case "expiryBeforeGrant": consent["expiresAtUtc"] = consent["grantedAtUtc"].AsDateTime.AddHours(-1); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }

    // AgentSchemaSamplingConsentGrant's constructor is internal to the trusted boundary (Core/Application/
    // Infrastructure/UnitTests); this test project is inside that boundary, so it constructs directly.
    private static AgentSchemaSamplingConsentGrant Consent(
        Guid principalId, Guid sourceGenerationId, AgentNamespaceScope scope,
        DateTimeOffset? grantedAtUtc = null, DateTimeOffset? expiresAtUtc = null) =>
        new(principalId, sourceGenerationId, scope, grantedAtUtc ?? DateTimeOffset.UtcNow, expiresAtUtc);

    private static AgentSchemaSamplingRequest Request(
        Guid principalId, Guid connectionId, Guid sourceGenerationId, string database, string collection) =>
        new(principalId, Guid.NewGuid(), Guid.NewGuid(), AgentOutputDestination.Local(), connectionId, sourceGenerationId,
            database, collection, 20, 1);

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SlopStudio.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public LiteDatabase OpenOffline() => new($"Filename={Path};Connection=direct");
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
