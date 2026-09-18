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
        var proposal = new AiChangeProposal(Guid.NewGuid(), original, original + ".limit(20)", "explicação", "+ alteração", false, "",
            new("limite a 20", "Aba", original, "javascript", "mongosh", "app", "users", "consulta"));
        Assert.That(proposal.IsNoOp, Is.False);
        Assert.That(AiDiffBuilder.Build(original, proposal.ProposedContent), Does.Contain("+"));
        var tab = new EsilvaSoft.SlopStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace) { Text = original, AiProposal = proposal };
        Assert.That(tab.Text, Is.EqualTo(original), "A proposta deve permanecer apenas na prévia até a confirmação.");
        Assert.That(tab.ApplyAiProposal(proposal), Is.True);
        Assert.That(tab.Text, Is.EqualTo(proposal.ProposedContent));

        var stale = proposal with { Id = Guid.NewGuid(), OriginalContent = proposal.ProposedContent, ProposedContent = "db.users.deleteMany({})" };
        tab.AiProposal = stale;
        tab.Text = "editor alterado pelo usuário";
        Assert.That(tab.ApplyAiProposal(stale), Is.False);
        Assert.That(tab.Text, Is.EqualTo("editor alterado pelo usuário"));
    }
}
