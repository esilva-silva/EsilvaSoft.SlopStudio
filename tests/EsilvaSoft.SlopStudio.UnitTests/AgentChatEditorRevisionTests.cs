using Avalonia.Headless;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Regression for ADR-055: the editor revision used by the agent chat lived in the removed per-tab assistant partial.
/// These tests use the real <see cref="WorkspaceTabViewModel"/> (no tab fixture) so the revision source is the tab itself.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class AgentChatEditorRevisionTests
{
    private const string Selection = "db.orders.find({})";

    private static Task<bool> RunOnUiAsync(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        return session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            await body();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public void EveryEditorChangeAdvancesTheRevisionPublishedToTheAgentSnapshot()
    {
        using var context = new WorkspaceTestContext();
        using var tab = new WorkspaceTabViewModel(context.Workspace) { Text = Selection };
        var before = tab.EditorRevision;
        var snapshotBefore = tab.CaptureAgentChatSnapshot();

        tab.Text = Selection + " ";
        tab.Text = Selection; // Restoring the same text is still a new revision.

        Assert.That(snapshotBefore.DocumentVersion, Is.EqualTo(before));
        Assert.That(tab.EditorRevision, Is.EqualTo(before + 2));
        Assert.That(tab.CaptureAgentChatSnapshot().DocumentVersion, Is.EqualTo(tab.EditorRevision));
    }

    [Test]
    public async Task EditingTheRealTabAfterReviewDiscardsTheReviewedSelectionPackage()
    {
        await RunOnUiAsync(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var tab = new WorkspaceTabViewModel(context.Workspace) { Text = Selection };
            tab.EditorSelectionProvider = () => Selection;
            var provider = new ScriptedAgentProvider("local");
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")),
                    new FakeAgentContextProvider(), null, null, null),
                tab.CaptureAgentChatSnapshot);
            chat.SelectedScope = chat.ContextScopes.Single(option => option.Scope == AgentContextScope.Selection);

            chat.ComposerText = "revise a seleção";
            await chat.ReviewCommand.ExecuteAsync(null);
            Assert.That(chat.HasPreview, Is.True);

            // Same selection text, but the document changed after the review: the package must not be sent.
            tab.Text = Selection + "\n// editado";
            await chat.SendCommand.ExecuteAsync(null);

            Assert.That(chat.HasPreview, Is.False);
            Assert.That(chat.StatusText, Does.Contain("prévia foi descartada"));
            Assert.That(provider.Sessions, Is.Empty, "A package reviewed against an older editor revision is never sent.");

            // Control: without an edit between review and send, the same flow is sent.
            await chat.ReviewCommand.ExecuteAsync(null);
            await chat.SendCommand.ExecuteAsync(null);
            Assert.That(provider.Sessions.Single().Requests.Single().AuthorizedContext, Does.Contain(Selection));
        });
    }
}
