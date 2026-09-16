using EsilvaSoft.SlopStudio.Application.Language;
using EsilvaSoft.SlopStudio.Application.Language.Context;

namespace EsilvaSoft.SlopStudio.UnitTests.Language.Context;

[TestFixture]
public sealed class ShapeWalkerTests
{
    [Test]
    public void FindFilterExpectsFieldsAndQueryOperatorsAtItsObjectKey()
    {
        var source = "db.orders.find({ ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.PropertyKey);
        Assert.That(result.ShapeId, Is.EqualTo("Filter"));
        Assert.That(result.ExpectedKinds & SymbolKinds.Field, Is.EqualTo(SymbolKinds.Field));
        Assert.That(result.ExpectedKinds & SymbolKinds.QueryOperator, Is.EqualTo(SymbolKinds.QueryOperator));
    }

    [Test]
    public void AggregateArrayExpectsStageFromPipelineShape()
    {
        var source = "db.orders.aggregate([ ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.ArrayElement);
        Assert.That(result.ShapeId, Is.EqualTo("Stage"));
        Assert.That(result.ExpectedKinds & SymbolKinds.AggregationStage, Is.EqualTo(SymbolKinds.AggregationStage));
    }

    [Test]
    public void FilterFieldConditionUsesOperatorObjectForNestedKey()
    {
        var source = "db.orders.find({ status: { ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.PropertyKey);
        Assert.That(result.ShapeId, Is.EqualTo("OperatorObject"));
        Assert.That(result.ExpectedKinds, Is.EqualTo(SymbolKinds.QueryOperator));
        Assert.That(result.ParentPath, Is.EqualTo("status"));
    }
}
