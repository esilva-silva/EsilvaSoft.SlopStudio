using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

#pragma warning disable CS0618 // Compatibility coverage for retired MQL suggestion API.

[TestFixture]
public sealed class MqlAutocompleteServiceTests
{
    private static readonly string[] ExpectedFieldPaths = ["name", "address", "address.city", "tags", "tags.name"];

    [Test]
    public void GetSuggestionsWithFieldPrefixReturnsObservedField()
    {
        var suggestions = MqlAutocompleteService.GetSuggestions("{ \"sta", ["status", "createdAt"]);

        Assert.That(suggestions, Does.Contain(new MqlSuggestion("status", "Campo observado nos resultados carregados", MqlSuggestionKind.Field)));
    }

    [Test]
    public void GetSuggestionsWithOperatorPrefixReturnsCompatibleOperators()
    {
        var suggestions = MqlAutocompleteService.GetSuggestions("{ \"age\": { \"$g");

        Assert.Multiple(() =>
        {
            Assert.That(suggestions.Select(suggestion => suggestion.Text), Does.Contain("$gt"));
            Assert.That(suggestions.Select(suggestion => suggestion.Text), Does.Contain("$gte"));
        });
    }

    [Test]
    public void GetAggregationSuggestionsReturnsOnlyAggregationStages()
    {
        var suggestions = MqlAutocompleteService.GetAggregationSuggestions("[{ \"$l");

        Assert.Multiple(() =>
        {
            Assert.That(suggestions.Select(suggestion => suggestion.Text), Does.Contain("$limit"));
            Assert.That(suggestions, Is.All.Matches<MqlSuggestion>(suggestion => suggestion.Kind == MqlSuggestionKind.AggregationStage));
        });
    }

    [Test]
    public void ApplySuggestionReplacesCurrentToken()
    {
        var result = MqlAutocompleteService.ApplySuggestion("{ \"age\": { \"$g", new MqlSuggestion("$gte", "Maior ou igual a", MqlSuggestionKind.QueryOperator));

        Assert.That(result, Is.EqualTo("{ \"age\": { \"$gte"));
    }

    [Test]
    public void ApplySuggestionCreatesFilterSkeletonFromEmptyFilter()
    {
        var result = MqlAutocompleteService.ApplySuggestion("{}", new MqlSuggestion("status", "Campo", MqlSuggestionKind.Field));

        Assert.That(result, Is.EqualTo("{\n  \"status\": null\n}"));
    }

    [Test]
    public void InferFieldPathsReturnsNestedAndArrayFields()
    {
        var fields = MqlAutocompleteService.InferFieldPaths(["{ \"name\": \"Ana\", \"address\": { \"city\": \"São Paulo\" }, \"tags\": [{ \"name\": \"vip\" }] }"]);

        Assert.That(fields, Is.SupersetOf(ExpectedFieldPaths));
    }

    [Test]
    public void InferJsonSchemaProducesBsonTypesAndNestedProperties()
    {
        var schema = MqlAutocompleteService.InferJsonSchema(["{ \"name\": \"Ana\", \"active\": true, \"address\": { \"city\": \"São Paulo\" }, \"tags\": [\"vip\"] }"]);

        Assert.Multiple(() =>
        {
            Assert.That(schema, Does.Contain("\"bsonType\": \"string\""));
            Assert.That(schema, Does.Contain("\"active\""));
            Assert.That(schema, Does.Contain("\"address\""));
            Assert.That(schema, Does.Contain("\"items\""));
        });
    }

    [Test]
    public void InferJsonSchemaRecognizesCanonicalExtendedJsonTypes()
    {
        var schema = MqlAutocompleteService.InferJsonSchema(["{ \"id\": { \"$oid\": \"507f1f77bcf86cd799439011\" }, \"createdAt\": { \"$date\": \"2026-09-08T00:00:00Z\" }, \"key\": { \"$binary\": { \"base64\": \"AAAAAAAAAAAAAAAAAAAAAA==\", \"subType\": \"04\" } } }"]);

        Assert.Multiple(() =>
        {
            Assert.That(schema, Does.Contain("\"objectId\""));
            Assert.That(schema, Does.Contain("\"date\""));
            Assert.That(schema, Does.Contain("\"binData\""));
        });
    }
}
#pragma warning restore CS0618
