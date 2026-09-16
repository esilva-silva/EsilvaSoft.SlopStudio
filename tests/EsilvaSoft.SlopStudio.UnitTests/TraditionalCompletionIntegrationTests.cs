using EsilvaSoft.SlopStudio.Application.Language;
using EsilvaSoft.SlopStudio.Application.Language.Completion;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class TraditionalCompletionIntegrationTests
{
    [Test]
    public async Task TabCapturesItsOwnSnapshotAndExposesCatalogSuggestions()
    {
        var context = new WorkspaceTestContext();
        var tab = new WorkspaceTabViewModel(context.Workspace)
        {
            Profile = ConnectionProfile.Create("Local", "mongodb://localhost"), Database = "shop", Text = "db."
        };
        tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));

        var result = await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None);

        Assert.That(result.Items.Select(item => item.Label), Does.Contain("getCollection"));
        Assert.That(result.Items.All(item => item.Edit.ReplaceRange.Start >= 0), Is.True);
        Assert.That(result.IsIncomplete, Is.False);
    }

    [Test]
    public async Task DetachedTabHasNoTraditionalProviderAndDoesNotThrow()
    {
        var tab = new WorkspaceTabViewModel(new WorkspaceTestContext().Workspace) { Text = "db." };
        Assert.That((await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None)).Items, Is.Empty);
    }

    [Test]
    public async Task WorkspaceResolvesTheCapturedProfileOnlyWhileQueryingMetadata()
    {
        using var context = new WorkspaceTestContext();
        var source = new RecordingCatalogSource();
        var catalog = new KnowledgeCatalog([source]);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, knowledgeCatalog: catalog);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        workspace.Profiles.Add(profile);
        var tab = new WorkspaceTabViewModel(context.Workspace) { TraditionalCompletion = workspace.TraditionalCompletion, Profile = profile, Database = "shop", Text = "db." };

        await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None);

        Assert.That(source.LastQuery!.Connection, Is.SameAs(profile));
        Assert.That(source.LastQuery.Access, Is.EqualTo(MetadataAccess.Peek));
    }

    [Test]
    public async Task TabKeepsSuggestionsWhenTheCatalogIsStillIncomplete()
    {
        using var context = new WorkspaceTestContext();
        var source = new RecordingCatalogSource { Completeness = CatalogCompleteness.Loading };
        var tab = new WorkspaceTabViewModel(context.Workspace) { Text = "db." };
        tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource(), source])));

        var result = await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None);

        Assert.That(result.Items, Is.Not.Empty);
        Assert.That(result.IsIncomplete, Is.True);
    }

    [Test]
    public void MetadataChangeRequestsARefreshOnlyForTheMatchingTabScope()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, Database = "shop", Collection = "orders" };
        var refreshes = 0;
        tab.TraditionalCompletionRefreshRequested += (_, _) => refreshes++;
        var identity = ConnectionIdentity.From(profile);

        tab.NotifyMetadataChanged(new(profile.Id, new MetadataKey(identity, MetadataScope.Definition, "shop", "orders")));
        tab.NotifyMetadataChanged(new(profile.Id, new MetadataKey(identity, MetadataScope.Definition, "other", "orders")));
        tab.NotifyMetadataChanged(new(Guid.NewGuid(), new MetadataKey(identity, MetadataScope.Definition, "shop", "orders")));

        Assert.That(refreshes, Is.EqualTo(1));
    }

    private sealed class RecordingCatalogSource : ICatalogSource
    {
        public SymbolKinds ProvidedKinds => SymbolKinds.Collection | SymbolKinds.DatabaseMethod;
        public CatalogCompleteness Completeness { get; init; } = CatalogCompleteness.Complete;
        public CatalogQuery? LastQuery { get; private set; }
        public CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Completeness;
        }
    }
}
