using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AdvancedAggregationTests
{
    [Test]
    public async Task ExplainKeepsSnapshotAndCancelsOnlyItsOwnTab()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        var firstReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new List<AggregationQuery>();
        context.Mongo.Handler = (method, args) =>
        {
            Assert.That(method, Is.EqualTo("ExplainAggregationAsync"));
            var query = (AggregationQuery)args[1]!; captured.Add(query);
            return (query.Database == "first" ? firstReply.Task : secondReply.Task).WaitAsync((CancellationToken)args[2]!);
        };
        var first = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, IsConnected = true, Database = "first", Collection = "orders", Mode = "Agregação", Text = "[{ $match: {} }]", Limit = 7 };
        var second = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, IsConnected = true, Database = "second", Collection = "customers", Mode = "Agregação", Text = "[]" };
        var runFirst = first.ExplainAggregationCommand.ExecuteAsync(null);
        var runSecond = second.ExplainAggregationCommand.ExecuteAsync(null);
        first.Database = "changed"; first.Collection = "changed"; first.Text = "[]"; first.Limit = 1;
        secondReply.SetResult("{\"queryPlanner\":{\"namespace\":\"second.customers\"}}");
        await runSecond;
        Assert.That(second.Messages, Does.Contain("Dev › second › customers"));
        Assert.That(first.IsRunning, Is.True);
        first.CancelCommand.Execute(null); await runFirst;
        Assert.That(first.Status, Is.EqualTo("Cancelado"));
        Assert.That(second.Messages, Does.Contain("second.customers"));
        Assert.That(captured[0], Is.EqualTo(new AggregationQuery("first", "orders", "[{ $match: {} }]", 7)));
        Assert.That(first.Results, Does.Not.Contain("queryPlanner"));
    }

    [TestCase("[\n { $match: {} },\n { $group: { _id: '$customer', total: { $sum: '$price' } } }\n]", true)]
    [TestCase("db.orders.aggregate([{ $match: { id: ObjectId('507f1f77bcf86cd799439011') } }]);", false)]
    [TestCase("[{ $set: { id: UUID('00112233-4455-6677-8899-aabbccddeeff'), exact: NumberLong('9223372036854775807') } }]", true)]
    public async Task OfflineValidationPreservesBsonConstructors(string text, bool aggregation)
    {
        var result = await new MongoCodeValidator().ValidateAsync(text, aggregation);
        Assert.That(result.IsValid, Is.True, result.Message);
    }

    [Test]
    public async Task OfflineValidationLocatesErrorsAndDoesNotExecuteCode()
    {
        const string text = "[\n { $match: {} },\n { $out: 'copy' }\n]";
        var result = await new MongoCodeValidator().ValidateAsync(text, true);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Offset, Is.EqualTo(text.IndexOf("$out", StringComparison.Ordinal)));
        Assert.That(result.Message, Does.Contain("Linha 3").And.Contain("estágio 2"));
        var syntax = await new MongoCodeValidator().ValidateAsync("db.orders.find({\n price: } )", false);
        Assert.That(syntax.IsValid, Is.False);
        Assert.That(syntax.Message, Does.Contain("linha 2"));
        Assert.That(syntax.Offset, Is.EqualTo("db.orders.find({\n price: ".Length));
        Assert.That((await new MongoCodeValidator().ValidateAsync("throw new Error('not executed')", false)).IsValid, Is.True);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => new MongoCodeValidator().ValidateAsync(text, true, cancellation.Token));
    }

    [TestCase("[{ $out: 'copy' }]", "$out")]
    [TestCase("[{ $merge: 'copy' }]", "$merge")]
    [TestCase("[{ $facet: { bad: [{ $out: 'copy' }] } }]", "$facet.bad")]
    [TestCase("[{ $lookup: { from: 'other', pipeline: [{ $merge: 'copy' }], as: 'rows' } }]", "$lookup.pipeline")]
    public void DirectAggregationRejectsWritesBeforeConnecting(string pipeline, string location)
    {
        var service = new MongoWorkspaceService();
        var profile = ConnectionProfile.Create("Leitura", "mongodb://127.0.0.1:1", isReadOnly: true);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.AggregateAsync(profile, new("sample", "orders", pipeline)));
        Assert.That(error!.Message, Does.Contain(location).And.Contain("não foi enviado"));
    }

    [TestCase("[{}]")]
    [TestCase("[5]")]
    [TestCase("[{ $match: {}, $limit: 2 }]")]
    [TestCase("[{ match: {} }]")]
    public void StructuralErrorsIdentifyStage(string pipeline)
    {
        var error = Assert.Throws<ArgumentException>(() => AggregationPipelineValidator.ValidateReadPipeline(BsonSerializer.Deserialize<BsonArray>(pipeline)));
        Assert.That(error!.Message, Does.Contain("estágio 1"));
    }

    [Test]
    public void LiteralOperatorNamesAndUnknownStagesRemainEditable()
    {
        var pipeline = BsonSerializer.Deserialize<BsonArray>("[{ $project: { value: { $literal: { $out: 'data' } } } }, { $futureStage: {} }]");
        Assert.DoesNotThrow(() => AggregationPipelineValidator.ValidateReadPipeline(pipeline));
    }

    [TestCase("$match")]
    [TestCase("$group")]
    [TestCase("$project")]
    [TestCase("$lookup")]
    [TestCase("$unwind")]
    [TestCase("$facet")]
    [TestCase("$sort")]
    [TestCase("$limit")]
    [TestCase("$skip")]
    [TestCase("$set")]
    [TestCase("$unset")]
    [TestCase("$count")]
    public void AllPhaseTwoStagesAreSuggestedAtStagePosition(string stage)
    {
        Assert.That(MqlAutocompleteService.GetAggregationSuggestions("[{ " + stage).Select(s => s.Text), Does.Contain(stage));
    }

    [Test]
    public void SuggestionsDistinguishStagesPredicatesAccumulatorsAndReferences()
    {
        Assert.That(MqlAutocompleteService.GetAggregationSuggestions("[{ $match: { $or: [{ age: { $g").Select(s => s.Text), Does.Contain("$gte").And.Not.Contain("$group"));
        Assert.That(MqlAutocompleteService.GetAggregationSuggestions("[{ $project: { value: { $cond: [{ $g").Select(s => s.Text), Does.Contain("$gte").And.Not.Contain("$group"));
        Assert.That(MqlAutocompleteService.GetAggregationSuggestions("[{ $facet: { page: [{ $s").Select(s => s.Text), Does.Contain("$sort"));
        Assert.That(MqlAutocompleteService.GetAggregationSuggestions("db.orders.aggregate([{ $match: { price: { $g").Select(s => s.Text), Does.Contain("$gte").And.Not.Contain("$group"));
        Assert.That(MqlAutocompleteService.GetAggregationSuggestions("[{ $group: { total: { $s").Select(s => s.Text), Does.Contain("$sum").And.Not.Contain("$sort"));
        Assert.That(MqlAutocompleteService.GetAggregationSuggestions("[{ $group: { _id: '$cu", ["customer", "price"]).Select(s => s.Text), Has.Exactly(1).EqualTo("$customer"));
        Assert.That(MqlAutocompleteService.GetAggregationSuggestions("[{ // $ma"), Is.Empty);
        Assert.That(MqlAutocompleteService.ApplySuggestion("[{ $match: {}, ", new("$limit", "", MqlSuggestionKind.AggregationStage)), Is.EqualTo("[{ $match: {}, $limit"));
    }
}
