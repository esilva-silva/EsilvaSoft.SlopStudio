using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public static class MqlAutocompleteService
{
    private static readonly MqlSuggestion[] Operators =
    [
        new("$eq", "Igual a", MqlSuggestionKind.QueryOperator),
        new("$ne", "Diferente de", MqlSuggestionKind.QueryOperator),
        new("$gt", "Maior que", MqlSuggestionKind.QueryOperator),
        new("$gte", "Maior ou igual a", MqlSuggestionKind.QueryOperator),
        new("$lt", "Menor que", MqlSuggestionKind.QueryOperator),
        new("$lte", "Menor ou igual a", MqlSuggestionKind.QueryOperator),
        new("$in", "Pertence à lista", MqlSuggestionKind.QueryOperator),
        new("$nin", "Não pertence à lista", MqlSuggestionKind.QueryOperator),
        new("$exists", "Campo existe", MqlSuggestionKind.QueryOperator),
        new("$regex", "Expressão regular", MqlSuggestionKind.QueryOperator),
        new("$and", "Conjunção de filtros", MqlSuggestionKind.QueryOperator),
        new("$or", "Disjunção de filtros", MqlSuggestionKind.QueryOperator),
        new("$set", "Define campo", MqlSuggestionKind.UpdateOperator),
        new("$unset", "Remove campo", MqlSuggestionKind.UpdateOperator),
        new("$inc", "Incrementa número", MqlSuggestionKind.UpdateOperator),
        new("$push", "Adiciona item ao array", MqlSuggestionKind.UpdateOperator),
        new("$match", "Filtra pipeline", MqlSuggestionKind.AggregationStage),
        new("$project", "Projeta campos", MqlSuggestionKind.AggregationStage),
        new("$group", "Agrupa documentos", MqlSuggestionKind.AggregationStage),
        new("$sort", "Ordena documentos", MqlSuggestionKind.AggregationStage),
        new("$limit", "Limita documentos", MqlSuggestionKind.AggregationStage),
        new("$lookup", "Relaciona outra coleção", MqlSuggestionKind.AggregationStage),
        new("$skip", "Pula documentos", MqlSuggestionKind.AggregationStage),
        new("$unwind", "Expande elementos de um array", MqlSuggestionKind.AggregationStage),
        new("$facet", "Executa subpipelines sobre a mesma entrada", MqlSuggestionKind.AggregationStage),
        new("$set", "Adiciona ou substitui campos no pipeline", MqlSuggestionKind.AggregationStage),
        new("$unset", "Remove campos no pipeline", MqlSuggestionKind.AggregationStage),
        new("$count", "Conta os documentos do pipeline", MqlSuggestionKind.AggregationStage),
        new("$expr", "Expressão em um filtro", MqlSuggestionKind.QueryOperator),
        new("$sum", "Soma valores", MqlSuggestionKind.AggregationExpression),
        new("$avg", "Média dos valores", MqlSuggestionKind.AggregationExpression),
        new("$min", "Menor valor", MqlSuggestionKind.AggregationExpression),
        new("$max", "Maior valor", MqlSuggestionKind.AggregationExpression),
        new("$push", "Acumula valores em um array", MqlSuggestionKind.AggregationExpression),
        new("$addToSet", "Acumula valores distintos", MqlSuggestionKind.AggregationExpression),
        new("$first", "Primeiro valor", MqlSuggestionKind.AggregationExpression),
        new("$last", "Último valor", MqlSuggestionKind.AggregationExpression),
        new("$cond", "Seleciona valor por condição", MqlSuggestionKind.AggregationExpression),
        new("$ifNull", "Valor alternativo para nulo ou ausente", MqlSuggestionKind.AggregationExpression),
        new("$literal", "Valor literal sem interpretar operadores", MqlSuggestionKind.AggregationExpression)
    ];

    [Obsolete("Use CompletionContextEngine and CompletionService. This compatibility API is not used by the editor completion list.")]
    public static IReadOnlyList<MqlSuggestion> GetSuggestions(string input, IEnumerable<string>? knownFields = null, int maximum = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        var prefix = GetCurrentPrefix(input);
        var suggestions = new List<MqlSuggestion>();

        if (!prefix.StartsWith('$'))
        {
            suggestions.AddRange((knownFields ?? [])
                .Where(field => field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .Select(field => new MqlSuggestion(field, "Campo observado nos resultados carregados", MqlSuggestionKind.Field)));
        }

        suggestions.AddRange(Operators.Where(@operator => @operator.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        return suggestions.Take(maximum).ToArray();
    }

    [Obsolete("Use CompletionContextEngine and CompletionService. This compatibility API is not used by the editor completion list.")]
    public static IReadOnlyList<MqlSuggestion> GetAggregationSuggestions(string input, IEnumerable<string>? knownFields = null, int maximum = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        var prefix = GetCurrentPrefix(input);
        var context = LegacyMqlSuggestionContext.Read(input);
        if (context.InComment) return [];
        var suggestions = new List<MqlSuggestion>();
        if (!context.StageKey)
        {
            suggestions.AddRange((knownFields ?? [])
                .Select(field => context.FieldReference ? "$" + field : field)
                .Where(field => field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .Select(field => new MqlSuggestion(field, "Campo conhecido no contexto do pipeline", MqlSuggestionKind.Field)));
        }
        if (!context.FieldReference)
            suggestions.AddRange(Operators.Where(op => (context.StageKey ? op.Kind == MqlSuggestionKind.AggregationStage
                : context.Stage == "$match" ? op.Kind == MqlSuggestionKind.QueryOperator
                : op.Kind is MqlSuggestionKind.AggregationExpression or MqlSuggestionKind.QueryOperator)
                && op.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        return suggestions.Take(maximum).ToArray();
    }

    [Obsolete("Use CompletionEdit from the traditional completion provider.")]
    public static string ApplySuggestion(string input, MqlSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(suggestion);

        if (string.Equals(input.Trim(), "{}", StringComparison.Ordinal))
        {
            return suggestion.Kind == MqlSuggestionKind.Field
                ? $"{{\n  \"{suggestion.Text}\": null\n}}"
                : $"{{\n  \"campo\": {{ \"{suggestion.Text}\": null }}\n}}";
        }

        var prefix = GetCurrentPrefix(input);
        return prefix.Length == 0 ? input + suggestion.Text : input[..^prefix.Length] + suggestion.Text;
    }

    public static IReadOnlySet<string> InferFieldPaths(IEnumerable<string> documents, int maximumDepth = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        // The catalog schema builder owns field inference; paths keep their discovery order.
        return new SchemaBuilder(maximumDepth, int.MaxValue).AddDocuments(documents).Build().Paths().ToHashSet(StringComparer.Ordinal);
    }

    public static string InferJsonSchema(IEnumerable<string> documents, int maximumDepth = 12) => JsonSchemaInference.Infer(documents, maximumDepth);

    private static string GetCurrentPrefix(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var end = input.Length;

        var start = end;

        while (start > 0 && IsTokenCharacter(input[start - 1]))
        {
            start--;
        }

        return input[start..end];
    }

    private static bool IsTokenCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '$' or '.';
}
