using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Context;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.SlopStudio.UnitTests.Language.Context;

[TestFixture]
public sealed class CompletionContextEngineTests
{
    [TestCase("db.", CompletionCursorRole.MemberAccess, SymbolKinds.Collection)]
    [TestCase("db.orders.find({ ", CompletionCursorRole.PropertyKey, SymbolKinds.Field)]
    [TestCase("db.orders.find({ state: ", CompletionCursorRole.PropertyValue, SymbolKinds.QueryOperator)]
    [TestCase("db.orders.aggregate([ ", CompletionCursorRole.ArrayElement, SymbolKinds.AggregationStage)]
    public void ClassifiesStructuralPositions(string source, CompletionCursorRole role, SymbolKinds expected)
    {
        var analysis = Analyze(source, EditorDialects.AggregationJson);
        Assert.That(analysis.Role, Is.EqualTo(role));
        Assert.That(analysis.Context.ExpectedKinds & expected, Is.EqualTo(expected));
        Assert.That(analysis.Context.CatalogAccess, Is.EqualTo(MetadataAccess.Peek));
    }

    [Test]
    public void DoesNotCompleteInsideCommentsNumbersOrRegex()
    {
        foreach (var source in new[] { "// note", "123", "/orders/" })
            Assert.That(Analyze(source).Role, Is.EqualTo(CompletionCursorRole.NonCompletable));
    }

    [Test]
    public void ClassifiesFieldAndVariableStringReferences()
    {
        var field = Analyze("{ value: \"$cus");
        var variable = Analyze("{ value: \"$$RO");
        Assert.That(field.Role, Is.EqualTo(CompletionCursorRole.FieldReferenceString));
        Assert.That(variable.Role, Is.EqualTo(CompletionCursorRole.VariableReferenceString));
        Assert.That(field.Quote, Is.EqualTo('"'));
    }

    [Test]
    public void CapturesPrefixAndReplaceSpan()
    {
        var analysis = Analyze("db.ord");
        Assert.That(analysis.Context.Prefix, Is.EqualTo("ord"));
        Assert.That(analysis.Context.ReplaceSpan, Is.EqualTo(new TextSpan(3, 3)));
        Assert.That(analysis.InsertSpan, Is.EqualTo(new TextSpan(3, 3)));
    }

    [Test]
    public void StringReplacementSpanExcludesItsDelimiters()
    {
        var analysis = Analyze("{ value: \"$to");
        Assert.That(analysis.Context.Prefix, Is.EqualTo("$to"));
        Assert.That(analysis.Context.ReplaceSpan, Is.EqualTo(new TextSpan(10, 3)));
    }

    [Test]
    public void ResolvesTheNamespaceInTheSameImmutableAnalysis()
    {
        var scope = new CatalogScope(new ConnectionIdentity(Guid.NewGuid(), "localhost", "fingerprint"), "app");
        var source = "db.orders.find({ ";
        var analysis = CompletionContextEngine.Analyze(new(new StringTextSnapshot(source), source.Length, EditorDialects.Console, scope));
        Assert.That(analysis.Target.Collection, Is.EqualTo("orders"));
        Assert.That(analysis.Target.Confidence, Is.EqualTo(NamespaceTargetConfidence.Explicit));
        Assert.That(analysis.Context.Scope, Is.EqualTo(scope with { Collection = "orders" }));
    }

    [Test]
    public void ExplicitCollectionOnDbDoesNotReuseTheTabCollection()
    {
        var connection = new ConnectionIdentity(Guid.NewGuid(), "localhost", "fingerprint");
        var tabScope = new CatalogScope(connection, "app", "Clientes");
        var source = "db.Pedidos.find({ ";
        var analysis = CompletionContextEngine.Analyze(new(
            new StringTextSnapshot(source), source.Length, EditorDialects.Console, tabScope));

        Assert.That(analysis.Context.Scope, Is.EqualTo(new CatalogScope(connection, "app", "Pedidos")));
    }

    [Test]
    public void UsesTheCatalogShapeWhenTheCallAndArgumentAreKnown()
    {
        var source = "db.orders.find({ status: { ";
        var analysis = Analyze(source);
        Assert.That(analysis.Context.ShapeId, Is.EqualTo("OperatorObject"));
        Assert.That(analysis.Context.ExpectedKinds, Is.EqualTo(SymbolKinds.QueryOperator));
        Assert.That(analysis.Context.ParentPath, Is.EqualTo("status"));
    }

    private static CompletionContextAnalysis Analyze(string source, EditorDialects dialect = EditorDialects.Console) =>
        CompletionContextEngine.Analyze(new(new StringTextSnapshot(source), source.Length, dialect, null));
}
