using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

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
