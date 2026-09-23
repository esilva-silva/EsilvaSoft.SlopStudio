using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AiChatTests
{
    [Test]
    public async Task ChatReturnsTheRequestedRecentDaysFilterAndLimitWithoutExecuting()
    {
        var context = new AiEditorContext(
            "Adicione um filtro para usuários criados nos últimos 30 dias e limite o resultado a 20 documentos.",
            "Consulta de usuários", "db.usuarios.find({ ativo: true })", "javascript", "mongosh", "app", "usuarios", "consulta");

        var response = await new AiChatService().AskAsync(new AiChatRequest(context));

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.ProposedCode, Is.EqualTo("db.usuarios.find({\n  ativo: true,\n  criadoEm: {\n    $gte: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000)\n  }\n}).limit(20)"));
        Assert.That(response.Diff, Does.Contain("-").And.Contain("+"));
        Assert.That(response.RequiresAdditionalConfirmation, Is.False);
    }

    [Test]
    public void ContextIsBoundedAndKeepsEveryStructuredField()
    {
        var context = new AiEditorContext(new string('i', 5000), new string('h', 600), new string('e', 2_000_000),
            "javascript", "mongosh", "app", "users", "consulta", new string('x', 10_000)).Bounded();

        Assert.That(context.Instruction, Has.Length.EqualTo(4096));
        Assert.That(context.Header, Has.Length.EqualTo(512));
        Assert.That(context.EditorContent, Has.Length.EqualTo(1_048_576));
        Assert.That(context.AdditionalContext, Has.Length.EqualTo(8192));
        Assert.That(context.Language, Is.EqualTo("javascript"));
        Assert.That(context.Dialect, Is.EqualTo("mongosh"));
        Assert.That(context.Database, Is.EqualTo("app"));
        Assert.That(context.Collection, Is.EqualTo("users"));
        Assert.That(context.OperationType, Is.EqualTo("consulta"));
    }

    [TestCase("db.users.updateMany({}, {$set:{ativo:false}})", "atualização", true)]
    [TestCase("db.users.deleteMany({})", "consulta", true)]
    [TestCase("db.users.find({})", "consulta", false)]
    public void WriteAndDestructiveProposalsRequireAnAdditionalConfirmation(string code, string operation, bool expected)
    {
        Assert.That(AiOperationRisk.Analyze(code, operation), Is.EqualTo(expected));
    }

    [Test]
    public async Task RiskyResponseIsMarkedForAnAdditionalConfirmation()
    {
        var response = await new AiChatService().AskAsync(new AiChatRequest(new(
            "Atualize os usuários", "Console", "db.users.updateMany({}, {$set:{ativo:false}})", "javascript", "mongosh", "app", "users", "atualização")));
        Assert.That(response!.RequiresAdditionalConfirmation, Is.True);
        Assert.That(response.Warning, Does.Contain("confirmação adicional"));
    }

    [Test]
    public void ApplyingAProposalIsExplicitAndRejectsStaleEditorText()
    {
        using var context = new WorkspaceTestContext();
        var original = "db.users.find({})";
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
            { Text = original, Database = "app", Collection = "users" };
        var proposed = original + ".limit(20)";
        var editorContext = tab.CaptureAiEditorContext("limite a 20");
        var proposal = new AiChangeProposal(Guid.NewGuid(), original, proposed, "explicação",
            AiDiffBuilder.Build(original, proposed), false, "", editorContext);
        Assert.That(proposal.IsNoOp, Is.False);
        Assert.That(proposal.Diff, Does.Contain("+"));
        tab.AiProposal = proposal;
        Assert.That(tab.Text, Is.EqualTo(original), "A proposta deve permanecer apenas na prévia até a confirmação.");
        Assert.That(tab.ApplyAiProposal(proposal), Is.True);
        Assert.That(tab.Text, Is.EqualTo(proposal.ProposedContent));

        var stale = proposal with { Id = Guid.NewGuid(), OriginalContent = proposal.ProposedContent, ProposedContent = "db.users.deleteMany({})" };
        tab.AiProposal = stale;
        tab.Text = "editor alterado pelo usuário";
        Assert.That(tab.ApplyAiProposal(stale), Is.False);
        Assert.That(tab.Text, Is.EqualTo("editor alterado pelo usuário"));
    }

    [Test]
    public async Task ChatContextHonorsEditorAndResultOptOutAndNeverIncludesInputJson()
    {
        using var context = new WorkspaceTestContext();
        var autocomplete = new AutocompleteService();
        await autocomplete.ConfigureAsync(new AutocompleteSettings { UseEditorContext = false, UseResultPanelContext = false });
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
        {
            Autocomplete = autocomplete,
            Text = "db.users.find({ segredo: 'editor' })",
            InputJson = "{\"token\":\"input-secret\"}",
            Database = "app",
            Collection = "users"
        };

        var snapshot = tab.CaptureAiEditorContext("resuma o contexto");

        Assert.That(snapshot.EditorContent, Is.Empty);
        Assert.That(snapshot.AdditionalContext, Does.Not.Contain("input-secret"));
        Assert.That(snapshot.AdditionalContext, Does.Not.Contain("segredo"));
        Assert.That(snapshot.Database, Is.EqualTo("app"));
        Assert.That(snapshot.Collection, Is.EqualTo("users"));
    }

    [Test]
    public async Task InputJsonIsIncludedOnlyAfterItsOwnExplicitLocalAiOptIn()
    {
        using var context = new WorkspaceTestContext();
        var autocomplete = new AutocompleteService();
        await autocomplete.ConfigureAsync(new AutocompleteSettings { LocalAiContextEnabled = true });
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
        {
            Autocomplete = autocomplete,
            InputJson = "{\"tenant\":\"north\"}",
            Database = "app",
            Collection = "users"
        };

        var defaultSnapshot = tab.CaptureAiEditorContext("explique o input");
        await autocomplete.ConfigureAsync(autocomplete.Settings with { IncludeInputJsonInLocalAiContext = true });
        var optedInSnapshot = tab.CaptureAiEditorContext("explique o input");

        Assert.That(defaultSnapshot.AdditionalContext, Does.Not.Contain("tenant"));
        Assert.That(optedInSnapshot.AdditionalContext, Does.Contain("INPUT JSON (opt-in)").And.Contain("tenant"));
    }

    [Test]
    public async Task GlobalAndPerConnectionOptOutPreventChatRequestDispatch()
    {
        using var context = new WorkspaceTestContext();
        var autocomplete = new AutocompleteService();
        await autocomplete.ConfigureAsync(new AutocompleteSettings { LocalAiContextEnabled = true });
        var chat = new RecordingAiChatService();
        var profile = ConnectionProfile.Create("Privada", "mongodb://localhost") with { LocalAiContextEnabled = false };
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
        {
            Autocomplete = autocomplete,
            AiChat = chat,
            Profile = profile,
            Database = "app",
            Collection = "users",
            Text = "db.users.find({})",
            ChatInput = "resuma"
        };

        await tab.SendAiChatCommand.ExecuteAsync(null);

        Assert.That(chat.RequestCount, Is.Zero);
        Assert.That(tab.ChatStatus, Is.EqualTo(AiChatStatus.NoContext));

        tab.Profile = profile with { LocalAiContextEnabled = true };
        await autocomplete.ConfigureAsync(autocomplete.Settings with { LocalAiContextEnabled = false });
        await tab.SendAiChatCommand.ExecuteAsync(null);
        Assert.That(chat.RequestCount, Is.Zero, "Global opt-out also blocks dispatch after this profile permits local context.");
    }

    [Test]
    public async Task ContextMustBeReviewedAndAnyLaterEditRequiresANewPreview()
    {
        using var context = new WorkspaceTestContext();
        var autocomplete = new AutocompleteService();
        await autocomplete.ConfigureAsync(new AutocompleteSettings { LocalAiContextEnabled = true });
        var chat = new RecordingAiChatService();
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
        {
            Autocomplete = autocomplete, AiChat = chat, Database = "app", Collection = "users",
            Text = "db.users.find({})", InputJson = "{\"key\":\"hidden\"}", ChatInput = "explique"
        };

        await tab.SendAiChatCommand.ExecuteAsync(null);
        Assert.That(tab.HasAiContextPreview, Is.True);
        Assert.That(tab.AiContextPreview, Does.Contain("db.users.find({})").And.Contain("explique").And.Not.Contain("hidden"));
        Assert.That(chat.RequestCount, Is.Zero);

        tab.Text += " ";
        await tab.SendAiChatCommand.ExecuteAsync(null);
        Assert.That(tab.HasAiContextPreview, Is.False, "Uma edição posterior invalida a prévia antiga.");
        Assert.That(chat.RequestCount, Is.Zero);

        await tab.SendAiChatCommand.ExecuteAsync(null);
        await autocomplete.ConfigureAsync(autocomplete.Settings with { UseEditorContext = false });
        await autocomplete.ConfigureAsync(autocomplete.Settings with { UseEditorContext = true });
        await tab.SendAiChatCommand.ExecuteAsync(null);
        Assert.That(tab.HasAiContextPreview, Is.False, "Alterar e restaurar a política também invalida a prévia.");
        Assert.That(chat.RequestCount, Is.Zero);

        await tab.SendAiChatCommand.ExecuteAsync(null);
        await tab.SendAiChatCommand.ExecuteAsync(null);
        Assert.That(chat.RequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvalidAiProposalIsRejectedBeforeItCanBeReviewedOrApplied()
    {
        using var context = new WorkspaceTestContext();
        var autocomplete = new AutocompleteService();
        await autocomplete.ConfigureAsync(new AutocompleteSettings { LocalAiContextEnabled = true });
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
        {
            Autocomplete = autocomplete,
            AiChat = new FixedAiChatService(new("explicação", "db.users.find({", "diff")),
            Database = "app",
            Collection = "users",
            Text = "db.users.find({})",
            ChatInput = "limite a 20"
        };

        await tab.SendAiChatCommand.ExecuteAsync(null); // revisão explícita do contexto
        await tab.SendAiChatCommand.ExecuteAsync(null);

        Assert.That(tab.AiProposal, Is.Null);
        Assert.That(tab.ChatStatus, Is.EqualTo(AiChatStatus.Error));
        Assert.That(tab.ChatStatusMessage, Does.Contain("inválido"));
        Assert.That(tab.Text, Is.EqualTo("db.users.find({})"));
    }

    [Test]
    public void ProposalCannotBeAppliedAfterTextWasEditedAndRestored()
    {
        using var context = new WorkspaceTestContext();
        var original = "db.users.find({})";
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
            { Text = original, Database = "app", Collection = "users" };
        var proposed = original + ".limit(20)";
        var proposal = new AiChangeProposal(Guid.NewGuid(), original, proposed, "explicação",
            AiDiffBuilder.Build(original, proposed), false, "", tab.CaptureAiEditorContext("limite a 20"));
        tab.AiProposal = proposal;

        tab.Text = original + " ";
        tab.Text = original;

        Assert.That(tab.CanApplyAiProposal(proposal), Is.False);
        Assert.That(tab.ApplyAiProposal(proposal), Is.False);
        Assert.That(tab.Text, Is.EqualTo(original));
    }

    [Test]
    public void ProposalCannotBeAppliedWhenItsDiffWasChanged()
    {
        using var context = new WorkspaceTestContext();
        var original = "db.users.find({})";
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
            { Text = original, Database = "app", Collection = "users" };
        var proposed = original + ".limit(20)";
        var proposal = new AiChangeProposal(Guid.NewGuid(), original, proposed, "explicação", "diff divergente", false, "",
            tab.CaptureAiEditorContext("limite a 20"));
        tab.AiProposal = proposal;

        Assert.That(tab.CanApplyAiProposal(proposal), Is.False);
        Assert.That(tab.ApplyAiProposal(proposal), Is.False);
        Assert.That(tab.Text, Is.EqualTo(original));
    }

    [Test]
    public void ProposalCannotBeAppliedAfterDestinationChangesAndReturnsToItsOriginalValue()
    {
        using var context = new WorkspaceTestContext();
        var original = "db.users.find({})";
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace)
            { Text = original, Database = "app", Collection = "users" };
        var proposed = original + ".limit(20)";
        var proposal = new AiChangeProposal(Guid.NewGuid(), original, proposed, "explicação",
            AiDiffBuilder.Build(original, proposed), false, "", tab.CaptureAiEditorContext("limite a 20"));
        tab.AiProposal = proposal;

        tab.Database = "outro";
        tab.Database = "app";

        Assert.That(tab.CanApplyAiProposal(proposal), Is.False);
        Assert.That(tab.ApplyAiProposal(proposal), Is.False);
    }

    private sealed class RecordingAiChatService : IAiChatService
    {
        public int RequestCount { get; private set; }
        public Task<AiChatResponse?> AskAsync(AiChatRequest request, CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return Task.FromResult<AiChatResponse?>(null);
        }
    }

    private sealed class FixedAiChatService(AiChatResponse response) : IAiChatService
    {
        public Task<AiChatResponse?> AskAsync(AiChatRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AiChatResponse?>(response);
    }
}
