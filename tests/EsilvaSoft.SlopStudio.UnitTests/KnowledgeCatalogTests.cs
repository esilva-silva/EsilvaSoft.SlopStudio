using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Language;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class NameTableTests
{
    [Test]
    public void PrefixIsCaseInsensitiveAndPrecedesHumpsAndSubstrings()
    {
        var table = new NameTable<string>(["clienteNome", "Cliente.Id", "createdAt", "nomeCliente", "CLIENTE_ATIVO", "id"], name => name);
        var matches = Collect(table, "cli");
        Assert.Multiple(() =>
        {
            Assert.That(matches.TakeWhile(match => match.Match == CatalogMatch.Prefix).Select(match => match.Name), Is.EqualTo(Expect.Words("Cliente.Id clienteNome CLIENTE_ATIVO")));
            Assert.That(matches.Single(match => match.Name == "nomeCliente").Match, Is.EqualTo(CatalogMatch.Substring));
            Assert.That(Collect(table, "cN").Single().Name, Is.EqualTo("clienteNome"));
            Assert.That(Collect(table, "cN").Single().Match, Is.EqualTo(CatalogMatch.Humps));
            Assert.That(Collect(table, ""), Has.Count.EqualTo(6));
            Assert.That(Collect(table, "", maximum: 2), Has.Count.EqualTo(2));
            Assert.That(table.TryGetExact("id", out var exact) && exact == "id", Is.True);
            Assert.That(table.TryGetExact("ID", out _), Is.False);
        });
    }

    [Test]
    public void FilteredItemsDoNotConsumeTheMaximum()
    {
        var table = new NameTable<string>(["a1", "a2", "a3", "a4"], name => name);
        var result = new List<string>();
        table.Collect("a", 2, name => name != "a1", (name, _) => result.Add(name));
        Assert.That(result, Is.EqualTo(Expect.Words("a2 a3")));
    }

    [Test]
    public void SearchKeyLetsOperatorsMatchWithoutDollar() =>
        Assert.That(Collect(new NameTable<string>(["$eq", "$exists", "$in"], name => name, name => name.TrimStart('$')), "e").Select(match => match.Name),
            Is.EqualTo(Expect.Words("$eq $exists")));

    [Test]
    public void LargeScopeReturnsOnlyMatchingPrefixRange()
    {
        var table = new NameTable<string>(Enumerable.Range(0, 10_000).Select(index => $"field{index:D5}"), name => name);
        Assert.That(Collect(table, "field0001").Select(match => match.Name), Is.EqualTo(Enumerable.Range(10, 10).Select(index => $"field{index:D5}")));
    }

    private static List<(string Name, CatalogMatch Match)> Collect(NameTable<string> table, string query, int maximum = 100)
    {
        var result = new List<(string, CatalogMatch)>();
        table.Collect(query, maximum, null, (name, match) => result.Add((name, match)));
        return result;
    }
}

[TestFixture]
public sealed class SchemaBuilderTests
{
    [Test]
    public void ValidatorDeclaresNestedRequiredArraysAndSafeEnums()
    {
        const string validator = """
            {"$jsonSchema":{"bsonType":"object","required":["Id","Cliente"],"properties":{
              "Id":{"bsonType":"binData"},
              "Status":{"enum":["ativo","inativo"]},
              "Cliente":{"bsonType":"object","required":["Nome"],"properties":{"Nome":{"bsonType":["string","null"]}}},
              "Itens":{"bsonType":"array","items":{"bsonType":"object","properties":{"Sku":{"bsonType":"string"}}}},
              "Segredo":{"enum":["password=abc"]}}}}
            """;
        var schema = new SchemaBuilder().AddJsonSchema(validator).Build();
        Assert.Multiple(() =>
        {
            Assert.That(schema.Evidence, Is.EqualTo(EvidenceSources.Validator));
            Assert.That(schema.Find("Id")!.Flags, Is.EqualTo(FieldTraits.Required));
            Assert.That(schema.Find("Cliente.Nome")!.Flags, Is.EqualTo(FieldTraits.Required));
            Assert.That(schema.Find("Cliente.Nome")!.Types.Keys, Is.EquivalentTo(Expect.Words("string null")));
            Assert.That(schema.Find("Status")!.EnumLiterals, Is.EqualTo(Expect.Words("ativo inativo")));
            Assert.That(schema.Find("Itens")!.Flags, Is.EqualTo(FieldTraits.Array | FieldTraits.ArrayOfDocuments));
            Assert.That(schema.Find("Itens.Sku")!.PrimaryType, Is.EqualTo("string"));
            Assert.That(schema.Find("Segredo")!.EnumLiterals, Is.Empty, "A literal recognized as a secret never enters the catalog.");
        });
    }

    [Test]
    public void IndexesAddPathsAndSkipTextAndWildcardKeys()
    {
        IndexInfo[] indexes =
        [
            ExplorerMetadataService.ParseIndex("""{"name":"a","key":{"Cliente.Id":1,"CriadoEm":-1}}"""),
            ExplorerMetadataService.ParseIndex("""{"name":"t","key":{"_fts":"text","_ftsx":1}}"""),
            ExplorerMetadataService.ParseIndex("""{"name":"w","key":{"meta.$**":1}}""")
        ];
        var schema = new SchemaBuilder().AddIndexes(indexes).Build();
        Assert.That(schema.Paths(), Is.EqualTo(Expect.Words("Cliente Cliente.Id CriadoEm")));
        Assert.That(schema.Find("Cliente.Id")!.Flags, Is.EqualTo(FieldTraits.Indexed));
        Assert.That(schema.Find("Cliente")!.Flags, Is.EqualTo(FieldTraits.None));
    }

    [Test]
    public void ResultsKeepDiscoveryOrderAndTypesButNeverValues()
    {
        string[] documents =
        [
            """{"_id":{"$oid":"0123456789abcdef01234567"},"Nome":"valor-privado","Itens":[{"Sku":"X"}],"Id":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"04"}}}""",
            """{"_id":{"$oid":"0123456789abcdef01234568"},"Nome":null,"Extra":1}""",
            "not json"
        ];
        var schema = new SchemaBuilder().AddDocuments(documents).Build();
        Assert.Multiple(() =>
        {
            Assert.That(schema.Paths(), Is.EqualTo(Expect.Words("_id Nome Itens Itens.Sku Id Extra")));
            Assert.That(schema.Find("Nome")!.Types, Is.EquivalentTo(new Dictionary<string, int> { ["string"] = 1, ["null"] = 1 }));
            Assert.That(schema.Find("_id")!.PrimaryType, Is.EqualTo("objectId"));
            Assert.That(schema.Find("Id")!.PrimaryType, Is.EqualTo("uuid"));
            Assert.That(schema.Find("Itens")!.Flags, Is.EqualTo(FieldTraits.Array | FieldTraits.ArrayOfDocuments));
            Assert.That(Serialize(schema), Does.Not.Contain("valor-privado").And.Not.Contain("0123456789abcdef"));
        });
    }

    [Test]
    public void SampleCountsOccurrenceOncePerDocumentAndMergesWithValidator()
    {
        SampledDocument[] sample =
        [
            new([new("Nome", "string", [], []), new("Itens", "array", [], [new("object", [new("Sku", "string", [], [])]), new("object", [new("Sku", "string", [], [])])])]),
            new([new("Nome", "string", [], [])])
        ];
        var sampled = new SchemaBuilder().AddSample(sample).Build();
        var validator = new SchemaBuilder().AddJsonSchema("""{"$jsonSchema":{"required":["Nome"],"properties":{"Nome":{"bsonType":"string"}}}}""").Build();
        var merged = CollectionSchema.Merge([validator, sampled, null]);
        Assert.Multiple(() =>
        {
            Assert.That(sampled.Find("Nome")!.Occurrence, Is.EqualTo(1));
            Assert.That(sampled.Find("Itens")!.Occurrence, Is.EqualTo(.5));
            Assert.That(sampled.Find("Itens.Sku")!.Occurrence, Is.EqualTo(.5));
            Assert.That(merged.SampleSize, Is.EqualTo(2));
            Assert.That(merged.Find("Nome")!.Evidence, Is.EqualTo(EvidenceSources.Validator | EvidenceSources.Sample));
            Assert.That(merged.Find("Nome")!.Flags, Is.EqualTo(FieldTraits.Required));
            Assert.That(merged.Find("Nome")!.Occurrence, Is.EqualTo(1));
        });
    }

    [Test]
    public void LimitsTruncateInsteadOfGrowing()
    {
        var wide = "{" + string.Join(",", Enumerable.Range(0, 50).Select(index => $"\"f{index}\":1")) + "}";
        var limited = new SchemaBuilder(maximumNodes: 10).AddDocuments([wide]).Build();
        var deep = new SchemaBuilder(maximumDepth: 2).AddDocuments(["""{"a":{"b":{"c":1}}}"""]).Build();
        Assert.Multiple(() =>
        {
            Assert.That(limited.NodeCount, Is.EqualTo(10));
            Assert.That(limited.IsTruncated, Is.True);
            Assert.That(deep.Paths(), Is.EqualTo(Expect.Words("a a.b")));
            Assert.That(deep.IsTruncated, Is.True);
        });
    }

    internal static string Serialize(CollectionSchema schema) =>
        JsonSerializer.Serialize(schema.Descendants().Select(node => new { node.Name, node.Path, node.Types, node.EnumLiterals, node.Flags }));
}

internal sealed class FakeMetadataSource : IMongoMetadataSource
{
    private int _calls;
    private int _completed;
    public int Calls => Volatile.Read(ref _calls);
    public int Completed => Volatile.Read(ref _completed);
    public TaskCompletionSource? Gate { get; set; }
    public bool IgnoreCancellation { get; set; }
    public Exception? Failure { get; set; }
    public Func<string, IReadOnlyList<string>> Databases { get; set; } = _ => ["loja", "auditoria"];
    public Func<string, IReadOnlyList<CollectionDefinition>> Collections { get; set; } = _ => [new("clientes", CollectionKind.Collection, null), new("pedidos", CollectionKind.View, null)];
    public IReadOnlyList<IndexInfo> Indexes { get; set; } = [];
    public IReadOnlyList<SampledDocument> Sample { get; set; } = [];

    public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Call(() => Databases(profile.Name), cancellationToken);
    public Task<IReadOnlyList<string>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<string>>(() => Collections(database).Select(definition => definition.Name).ToArray(), cancellationToken);
    public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
        Call(() => Collections(database).SingleOrDefault(definition => definition.Name == collection), cancellationToken);
    public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => Call(() => Indexes, cancellationToken);
    public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) =>
        Call(() => Sample, cancellationToken);

    private async Task<T> Call<T>(Func<T> value, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        try
        {
            if (Gate is { } gate) await (IgnoreCancellation ? gate.Task : gate.Task.WaitAsync(cancellationToken));
            if (Failure is { } failure) throw failure;
            return value();
        }
        finally { Interlocked.Increment(ref _completed); }
    }
}

internal sealed class MetadataClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

[TestFixture]
public sealed class MetadataCacheTests
{
    private static readonly ConnectionProfile Profile = ConnectionProfile.Create("servidor-alfa", "mongodb://host");
    private static ConnectionIdentity Identity => ConnectionIdentity.From(Profile);

    [Test]
    public void DisconnectedProfilesAndPeekNeverLoad()
    {
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source);
        Assert.That(cache.GetDatabases(Identity).Freshness, Is.EqualTo(MetadataFreshness.Unknown));
        cache.Connect(Profile);
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Unknown));
        Assert.That(source.Calls, Is.Zero);
    }

    [Test]
    public async Task ConcurrentReadsShareOneLoadAndNeverBlock()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var views = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => cache.GetDatabases(Identity))));
        Assert.That(views.Select(view => view.Value), Has.All.Null, "Reads return immediately while the load is blocked.");
        Assert.That(views.Select(view => view.Freshness), Has.All.EqualTo(MetadataFreshness.Loading));
        await WaitUntilAsync(() => source.Calls == 1);
        source.Gate.SetResult();
        await cache.RefreshAsync(new(Identity, MetadataScope.Databases));
        var loaded = cache.GetDatabases(Identity);
        Assert.That(loaded.Value, Is.EqualTo(Expect.Words("loja auditoria")));
        Assert.That(loaded.Freshness, Is.EqualTo(MetadataFreshness.Fresh));
        Assert.That(source.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task StaleValueIsServedWhileASingleRefreshRuns()
    {
        var clock = new MetadataClock();
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source, timeProvider: clock);
        cache.Connect(Profile);
        var key = new MetadataKey(Identity, MetadataScope.Databases);
        await cache.RefreshAsync(key);
        clock.Now += TimeSpan.FromMinutes(6);
        source.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Databases = _ => ["loja"];
        var stale = cache.GetDatabases(Identity);
        var again = cache.GetDatabases(Identity);
        Assert.Multiple(() =>
        {
            Assert.That(stale.Value, Is.EqualTo(Expect.Words("loja auditoria")));
            Assert.That(stale.Freshness, Is.EqualTo(MetadataFreshness.Stale));
            Assert.That(stale.IsRefreshing, Is.True);
            Assert.That(again.Value, Is.Not.Null);
        });
        await WaitUntilAsync(() => source.Calls == 2);
        source.Gate.SetResult();
        await cache.RefreshAsync(key);
        Assert.That(cache.GetDatabases(Identity).Value, Is.EqualTo(Expect.Words("loja")));
        Assert.That(source.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task FailuresBackOffAndKeepThePreviousValueStale()
    {
        var clock = new MetadataClock();
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source, timeProvider: clock);
        cache.Connect(Profile);
        var key = new MetadataKey(Identity, MetadataScope.Databases);
        await cache.RefreshAsync(key);
        clock.Now += TimeSpan.FromMinutes(6);
        source.Failure = new InvalidOperationException("host privado");
        await cache.RefreshAsync(key);
        var failed = cache.GetDatabases(Identity);
        Assert.That(failed.Value, Is.Not.Null);
        Assert.That(failed.Freshness, Is.EqualTo(MetadataFreshness.Stale));
        Assert.That(source.Calls, Is.EqualTo(2), "Within the backoff a read never retries.");
        clock.Now += TimeSpan.FromSeconds(31);
        source.Failure = null;
        cache.GetDatabases(Identity);
        await WaitUntilAsync(() => source.Calls == 3);
        var missing = new MetadataKey(Identity, MetadataScope.Collections, "loja");
        source.Failure = new InvalidOperationException();
        await cache.RefreshAsync(missing);
        Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Failed));
    }

    [Test]
    public void InvalidationsRemoveOrMarkExactlyTheAffectedKeys()
    {
        var bus = new MetadataInvalidationBus();
        using var cache = new MetadataCache(new FakeMetadataSource(), invalidations: bus);
        var index = ExplorerMetadataService.ParseIndex("""{"name":"email_1","key":{"email":1}}""");
        cache.PutDatabases(Profile, ["loja", "auditoria"]);
        cache.PutCollections(Profile, "loja", ["clientes", "pedidos"]);
        cache.PutCollections(Profile, "auditoria", ["eventos"]);
        cache.PutIndexes(Profile, "loja", "clientes", [index]);
        cache.PutIndexes(Profile, "loja", "pedidos", [index]);

        bus.Publish(new(Profile.Id, MetadataChange.Collections, "loja", "clientes"));
        Assert.Multiple(() =>
        {
            Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Value, Is.Null);
            Assert.That(cache.GetIndexes(Identity, "loja", "clientes", MetadataAccess.Peek).Value, Is.Null);
            Assert.That(cache.GetIndexes(Identity, "loja", "pedidos", MetadataAccess.Peek).Value, Is.Not.Null);
            Assert.That(cache.GetCollections(Identity, "auditoria", MetadataAccess.Peek).Value, Is.Not.Null);
            Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Fresh));
        });

        bus.Publish(new(Profile.Id, MetadataChange.Databases, Strength: InvalidationStrength.Soft));
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Stale));
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Value, Is.Not.Null);

        bus.Publish(new(Guid.NewGuid(), MetadataChange.Connection));
        Assert.That(cache.GetIndexes(Identity, "loja", "pedidos", MetadataAccess.Peek).Value, Is.Not.Null, "Another profile is never affected.");
        bus.Publish(new(Profile.Id, MetadataChange.Connection));
        Assert.That(cache.SnapshotNamespaces(100), Is.Empty);
    }

    [Test]
    public async Task DisconnectCancelsLoadsAndDiscardsLateResults()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreCancellation = true };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        cache.GetDatabases(Identity);
        await WaitUntilAsync(() => source.Calls == 1);
        cache.Disconnect(Profile.Id);
        source.Gate.SetResult();
        await WaitUntilAsync(() => source.Completed == 1);
        await Task.Delay(50);
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Value, Is.Null);
        Assert.That(cache.IsConnected(Identity), Is.False);
    }

    [Test]
    public void WriteThroughDropsRemovedScopesAndEvictsLeastRecentlyUsedCollections()
    {
        using var cache = new MetadataCache(new FakeMetadataSource(), options: new() { CollectionScopedEntriesPerConnection = 2 });
        var index = ExplorerMetadataService.ParseIndex("""{"name":"_id_","key":{"_id":1}}""");
        cache.PutDatabases(Profile, ["a", "b"]);
        cache.PutCollections(Profile, "a", ["c1", "c2", "c3"]);
        cache.PutIndexes(Profile, "a", "c1", [index]);
        cache.PutIndexes(Profile, "a", "c2", [index]);
        cache.GetIndexes(Identity, "a", "c1", MetadataAccess.Peek);
        cache.PutIndexes(Profile, "a", "c3", [index]);
        Assert.Multiple(() =>
        {
            Assert.That(cache.GetIndexes(Identity, "a", "c2", MetadataAccess.Peek).Value, Is.Null, "Least recently used entry is evicted.");
            Assert.That(cache.GetIndexes(Identity, "a", "c1", MetadataAccess.Peek).Value, Is.Not.Null);
            Assert.That(cache.GetIndexes(Identity, "a", "c3", MetadataAccess.Peek).Value, Is.Not.Null);
        });
        cache.PutDatabases(Profile, ["b"]);
        Assert.That(cache.GetCollections(Identity, "a", MetadataAccess.Peek).Value, Is.Null);
        Assert.That(cache.GetIndexes(Identity, "a", "c1", MetadataAccess.Peek).Value, Is.Null);
    }

    [Test]
    public async Task DefinitionsLoadPerCollectionWithValidatorAndKind()
    {
        var source = new FakeMetadataSource
        {
            Collections = _ => [new("clientes", CollectionKind.Collection, """{"$jsonSchema":{"properties":{"Nome":{"bsonType":"string"}}}}"""), new("resumo", CollectionKind.View, null)]
        };
        using var cache = new MetadataCache(source);
        cache.PutCollections(Profile, "loja", ["clientes", "resumo"]);
        cache.Connect(Profile);
        await cache.RefreshAsync(new(Identity, MetadataScope.Definition, "loja", "clientes"));
        await cache.RefreshAsync(new(Identity, MetadataScope.Definition, "loja", "resumo"));
        Assert.That(cache.GetDefinition(Identity, "loja", "clientes", MetadataAccess.Peek).Value!.Validator!.Find("Nome")!.PrimaryType, Is.EqualTo("string"));
        Assert.That(cache.GetDefinition(Identity, "loja", "resumo", MetadataAccess.Peek).Value!.Validator, Is.Null);
        Assert.That(source.Calls, Is.EqualTo(2), "Only the requested collections are loaded, never every validator of the database.");
        Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Value!.Single(entry => entry.Name == "resumo").Kind, Is.EqualTo(CollectionKind.View));
        cache.PutCollections(Profile, "loja", ["clientes", "resumo"]);
        Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Value!.Single(entry => entry.Name == "resumo").Kind, Is.EqualTo(CollectionKind.View),
            "Explorer names keep kinds already known.");
    }

    [Test]
    public async Task SchemaSamplingRequiresOptInOrAnExplicitAction()
    {
        var source = new FakeMetadataSource { Sample = [new([new("Nome", "string", [], [])])] };
        var operations = new ApplicationOperationService();
        using var cache = new MetadataCache(source, operations);
        cache.Connect(Profile);
        Assert.That(cache.GetSampledSchema(Identity, "loja", "clientes").Freshness, Is.EqualTo(MetadataFreshness.Unknown));
        Assert.That(source.Calls, Is.Zero);

        var explicitSchema = await cache.SampleSchemaAsync(Profile, "loja", "pedidos");
        Assert.That(explicitSchema.Find("Nome"), Is.Not.Null);
        Assert.That(cache.GetSampledSchema(Identity, "loja", "pedidos", MetadataAccess.Peek).Value, Is.SameAs(explicitSchema));
        Assert.That(operations.LastCompleted!.Description, Does.Contain("somente nomes e tipos"));

        cache.SetSchemaSamplingAllowed(Profile.Id, true);
        cache.GetSampledSchema(Identity, "loja", "clientes");
        await WaitUntilAsync(() => cache.GetSampledSchema(Identity, "loja", "clientes", MetadataAccess.Peek).Value is not null);
        Assert.That(source.Calls, Is.EqualTo(2));
        Assert.That(cache.SchemaSamplingProfiles, Is.EqualTo(new[] { Profile.Id }));
    }

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Condição não atingida em 5 s.");
            await Task.Delay(10);
        }
    }
}

[TestFixture]
public sealed class KnowledgeCatalogTests
{
    [Test]
    public void LanguageMatchesOperatorsWithoutDollarAndRespectsDialect()
    {
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource()]);
        var operators = catalog.Query(new(SymbolKinds.QueryOperator, EditorDialects.Console, "e"));
        Assert.Multiple(() =>
        {
            Assert.That(operators.Candidates.Select(candidate => candidate.Symbol.Name), Does.Contain("$eq").And.Contain("$exists").And.Contain("$elemMatch"));
            Assert.That(operators.Candidates.Select(candidate => candidate.Symbol.Kind), Has.All.EqualTo(SymbolKind.QueryOperator));
            Assert.That(operators.Completeness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(catalog.Query(new(SymbolKinds.CollectionMethod, EditorDialects.Console, "find")).Candidates.Select(candidate => candidate.Symbol.Name),
                Does.Contain("findOne").And.Not.Contain("findOneAndUpdate"));
            Assert.That(catalog.Query(new(SymbolKinds.CollectionMethod, EditorDialects.MongoshScript, "find")).Candidates.Select(candidate => candidate.Symbol.Name),
                Does.Contain("findOneAndUpdate"));
            Assert.That(catalog.Query(new(SymbolKinds.QueryOperator, EditorDialects.Console) { MaximumCandidates = 3 }).Candidates, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void MetadataIsAnsweredFromMemoryAndReportsUnavailableScopes()
    {
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("servidor-alfa", "mongodb://host");
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)]);
        var unavailable = catalog.Query(new(SymbolKinds.Collection, EditorDialects.Console) { Connection = profile, Database = "loja" });
        Assert.That(unavailable.Completeness, Is.EqualTo(CatalogCompleteness.Unavailable));
        cache.PutCollections(profile, "loja", ["clientes", "pedidos"]);
        var collections = catalog.Query(new(SymbolKinds.Collection, EditorDialects.Console, "cli") { Connection = profile, Database = "loja" });
        Assert.Multiple(() =>
        {
            Assert.That(collections.Candidates.Select(candidate => candidate.Symbol.Name), Is.EqualTo(Expect.Words("clientes")));
            Assert.That(collections.Completeness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(catalog.Query(new(SymbolKinds.Connection, EditorDialects.Console, "serv") { Connections = [profile] }).Candidates.Single().Symbol.Name, Is.EqualTo("servidor-alfa"));
            Assert.That(source.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task FieldsMergeValidatorIndexesAndTabResultsAtTheRequestedPath()
    {
        var source = new FakeMetadataSource
        {
            Collections = _ => [new("Clientes", CollectionKind.Collection,
                """{"$jsonSchema":{"required":["Id"],"properties":{"Id":{"bsonType":"binData"},"Cliente":{"bsonType":"object","properties":{"Nome":{"bsonType":"string"}}}}}}""")],
            Indexes = [ExplorerMetadataService.ParseIndex("""{"name":"c","key":{"Cliente.Id":1}}""")]
        };
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("servidor-alfa", "mongodb://host");
        var identity = ConnectionIdentity.From(profile);
        cache.Connect(profile);
        await cache.RefreshAsync(new(identity, MetadataScope.Definition, "Projetos", "Clientes"));
        await cache.RefreshAsync(new(identity, MetadataScope.Indexes, "Projetos", "Clientes"));
        var local = new SchemaBuilder().AddDocuments(["""{"Cliente":{"Email":"valor-local"}}"""]).Build();
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)]);
        var query = new CatalogQuery(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = "Projetos", Collection = "Clientes", LocalSchemas = [local] };
        var children = catalog.Query(query with { ParentPath = "Cliente" });
        var root = catalog.Query(query with { Prefix = "i" });
        Assert.Multiple(() =>
        {
            Assert.That(children.Candidates.Select(candidate => candidate.Symbol.Name), Is.EquivalentTo(Expect.Words("Nome Id Email")));
            Assert.That(children.Completeness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(children.Candidates.Single(candidate => candidate.Symbol.Name == "Id").Symbol.Detail, Does.Contain("indexado"));
            Assert.That(children.Candidates.Single(candidate => candidate.Symbol.Name == "Email").Symbol.Evidence, Is.EqualTo(EvidenceSources.Results));
            Assert.That(root.Candidates[0].Symbol.Name, Is.EqualTo("Id"));
            Assert.That(root.Candidates[0].Symbol.Detail, Does.Contain("obrigatório"));
            Assert.That(children.Candidates.Select(candidate => candidate.Symbol.Kind), Has.All.EqualTo(SymbolKind.Field), "A field query never consults methods or operators.");
            Assert.That(source.Calls, Is.EqualTo(2), "Fresh scopes are not reloaded and sampling is not automatic.");
        });
    }
}

[TestFixture, NonParallelizable]
public sealed class AutocompleteMetricsTests
{
    [Test]
    public async Task InstrumentsCarryOnlyAllowedTagsWithoutUserText()
    {
        var recorded = new ConcurrentBag<(string Instrument, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) => { if (instrument.Meter.Name == AutocompleteMetrics.MeterName) meterListener.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => recorded.Add((instrument.Name, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => recorded.Add((instrument.Name, tags.ToArray())));
        listener.Start();

        var source = new FakeMetadataSource { Collections = _ => [new("colecao-secreta", CollectionKind.Collection, """{"$jsonSchema":{"properties":{"campo-secreto":{"bsonType":"string"}}}}""")] };
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("conexao-secreta", "mongodb://secret-host");
        cache.Connect(profile);
        await cache.RefreshAsync(new(ConnectionIdentity.From(profile), MetadataScope.Definition, "banco-secreto", "colecao-secreta"));
        new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)])
            .Query(new(SymbolKinds.Field | SymbolKinds.QueryOperator, EditorDialects.Console, "campo") { Connection = profile, Database = "banco-secreto", Collection = "colecao-secreta" });
        using var session = new CompletionSession();
        await session.RequestAsync(new AutocompleteService(), new AutocompleteRequest("db.getCollection('colecao-secreta').find({ campo-secre", "valor-secreto"), immediate: true);

        Assert.That(recorded.Select(item => item.Instrument), Does.Contain("catalog.query.duration").And.Contain("metadata.refresh.duration")
            .And.Contain("metadata.cache.lookup").And.Contain("completion.requested"));
        foreach (var (instrument, tags) in recorded)
            foreach (var tag in tags)
            {
                Assert.That(AutocompleteMetrics.AllowedTags, Does.Contain(tag.Key), instrument);
                Assert.That(tag.Value?.ToString() ?? "", Does.Not.Contain("secret"), instrument);
            }
    }
}

[TestFixture]
public sealed class AutocompleteArchitectureTests
{
    [Test]
    public void ApplicationDependsOnlyOnTheBaseLibraryAndCore()
    {
        var references = typeof(LanguageDefinition).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.That(references.Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name is not ("netstandard" or "mscorlib" or "EsilvaSoft.SlopStudio.Core")), Is.Empty);
    }

    [Test]
    public void LanguageLayerNeverReferencesTheAiRuntime()
    {
        Type[] forbidden = [typeof(ILocalAiModelService), typeof(ILocalModelRuntime), typeof(ITokenizer), typeof(IAutocompleteService)];
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offenders = typeof(LanguageDefinition).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(LanguageDefinition).Namespace)
            .SelectMany(type => type.GetFields(all).Select(field => (type, field.FieldType))
                .Concat(type.GetProperties(all).Select(property => (type, property.PropertyType)))
                .Concat(type.GetMethods(all).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used)))
                .Concat(type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters()).Select(parameter => (type, parameter.ParameterType))))
            .Where(pair => forbidden.Contains(pair.Item2))
            .Select(pair => pair.type.FullName + " → " + pair.Item2.Name)
            .ToArray();
        Assert.That(offenders, Is.Empty);
    }
}

[TestFixture]
public sealed class MongoMetadataSourceTests
{
    [Test]
    public void SamplePipelineProjectsOnlyNamesAndTypesToTheRequestedDepth()
    {
        var pipeline = MongoMetadataSource.BuildSamplePipeline(new SchemaSampleOptions(Size: 50, Depth: 3));
        Assert.That(pipeline[0], Is.EqualTo(BsonDocument.Parse("{ $sample: { size: 50 } }")));
        var projection = pipeline[1]["$project"].AsBsonDocument;
        Assert.That(projection.Names, Is.EqualTo(Expect.Words("_id f")));
        var depth = 0;
        Visit(projection["f"], 0);
        Assert.That(depth, Is.EqualTo(3));

        // Depth is the nesting of $map bodies whose input expands a document with $objectToArray.
        void Visit(BsonValue value, int level)
        {
            if (value.IsBsonArray) { foreach (var item in value.AsBsonArray) Visit(item, level); return; }
            if (!value.IsBsonDocument) return;
            var document = value.AsBsonDocument;
            if (document.TryGetValue("input", out var input) && input.IsBsonDocument && input.AsBsonDocument.Contains("$objectToArray"))
            {
                level++;
                depth = Math.Max(depth, level);
            }
            // Emitted objects carry only the name, the $type and nested structure: no expression returns a field value.
            if (document.Contains("k")) Assert.That(document.Names, Is.SubsetOf(Expect.Words("k t c e")));
            if (document.Contains("t") && document["t"].IsBsonDocument) Assert.That(document["t"].AsBsonDocument.Names, Is.EqualTo(Expect.Words("$type")));
            foreach (var element in document) Visit(element.Value, level);
        }
    }

    [Test]
    public void SampleDocumentsParseIntoNestedFieldsAndElements()
    {
        var document = BsonDocument.Parse("""
            { "f": [ { "k": "_id", "t": "objectId" },
                     { "k": "Cliente", "t": "object", "c": [ { "k": "Nome", "t": "string" } ] },
                     { "k": "Itens", "t": "array", "e": [ { "t": "object", "c": [ { "k": "Sku", "t": "string" } ] }, { "t": "int" } ] } ] }
            """);
        var schema = new SchemaBuilder().AddSample([MongoMetadataSource.ParseSample(document)]).Build();
        Assert.That(schema.Paths(), Is.EqualTo(Expect.Words("_id Cliente Cliente.Nome Itens Itens.Sku")));
        Assert.That(schema.Find("Itens")!.Flags, Is.EqualTo(FieldTraits.Array | FieldTraits.ArrayOfDocuments));
        Assert.That(schema.Find("Cliente.Nome")!.Occurrence, Is.EqualTo(1));
    }

    [TestCase(0, 4, 2000)]
    [TestCase(100, 9, 2000)]
    [TestCase(100, 4, 0)]
    public void SampleOptionsAreBounded(int size, int depth, int maxTime) =>
        Assert.Throws<ArgumentException>(() => new SchemaSampleOptions(size, depth, maxTime).Validate());
}

internal static class Expect
{
    /// <summary>Expected names as a space-separated list, avoiding constant array arguments.</summary>
    public static string[] Words(string words) => words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
