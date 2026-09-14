using System.Text.Json;
using System.Text.Json.Nodes;
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

    public static IReadOnlyList<MqlSuggestion> GetAggregationSuggestions(string input, IEnumerable<string>? knownFields = null, int maximum = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        var prefix = GetCurrentPrefix(input);
        var context = AggregationCompletionContext.Read(input);
        if (context.InComment) return [];
        var suggestions = new List<MqlSuggestion>();
        if (!context.StageKey)
        {
            suggestions.AddRange((knownFields ?? [])
                .Select(field => context.FieldReference ? "$" + field : field)
                .Where(field => field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .Select(field => new MqlSuggestion(field, "Campo observado nos resultados carregados", MqlSuggestionKind.Field)));
        }
        if (!context.FieldReference)
            suggestions.AddRange(Operators.Where(op => (context.StageKey ? op.Kind == MqlSuggestionKind.AggregationStage
                : context.Stage == "$match" ? op.Kind == MqlSuggestionKind.QueryOperator
                : op.Kind is MqlSuggestionKind.AggregationExpression or MqlSuggestionKind.QueryOperator)
                && op.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        return suggestions.Take(maximum).ToArray();
    }

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
        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            try
            {
                using var json = JsonDocument.Parse(document);
                AddFieldPaths(json.RootElement, null, 0, maximumDepth, paths);
            }
            catch (JsonException)
            {
                // Um documento inválido não deve impedir autocomplete baseado nos demais resultados.
            }
        }

        return paths;
    }

    public static string InferJsonSchema(IEnumerable<string> documents, int maximumDepth = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        var root = new SchemaNode();
        foreach (var document in documents)
        {
            try
            {
                using var json = JsonDocument.Parse(document);
                AddSchema(json.RootElement, root, 0, maximumDepth);
            }
            catch (JsonException)
            {
                // A inferência permanece útil quando um documento isolado não pode ser lido.
            }
        }

        return ToSchema(root, true).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

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

    private static void AddFieldPaths(JsonElement element, string? parent, int depth, int maximumDepth, ISet<string> paths)
    {
        if (depth >= maximumDepth)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetExtendedJsonType(element, out _)) return;
            foreach (var property in element.EnumerateObject())
            {
                var current = string.IsNullOrEmpty(parent) ? property.Name : $"{parent}.{property.Name}";
                paths.Add(current);
                AddFieldPaths(property.Value, current, depth + 1, maximumDepth, paths);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AddFieldPaths(item, parent, depth + 1, maximumDepth, paths);
            }
        }
    }

    private static void AddSchema(JsonElement element, SchemaNode node, int depth, int maximumDepth)
    {
        if (depth >= maximumDepth)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryGetExtendedJsonType(element, out var extendedType))
                {
                    node.Types.Add(extendedType);
                    return;
                }

                node.Types.Add("object");
                foreach (var property in element.EnumerateObject())
                {
                    if (!node.Properties.TryGetValue(property.Name, out var child))
                    {
                        child = new SchemaNode();
                        node.Properties[property.Name] = child;
                    }

                    AddSchema(property.Value, child, depth + 1, maximumDepth);
                }

                break;
            case JsonValueKind.Array:
                node.Types.Add("array");
                node.Items ??= new SchemaNode();
                foreach (var item in element.EnumerateArray())
                {
                    AddSchema(item, node.Items, depth + 1, maximumDepth);
                }

                break;
            case JsonValueKind.String: node.Types.Add("string"); break;
            case JsonValueKind.Number: node.Types.Add("double"); break;
            case JsonValueKind.True:
            case JsonValueKind.False: node.Types.Add("bool"); break;
            case JsonValueKind.Null: node.Types.Add("null"); break;
        }
    }

    private static bool TryGetExtendedJsonType(JsonElement element, out string type)
    {
        type = string.Empty;
        var properties = element.EnumerateObject().ToArray();
        if (properties.Length != 1)
        {
            return false;
        }

        type = properties[0].Name switch
        {
            "$oid" => "objectId",
            "$date" => "date",
            "$numberInt" => "int",
            "$numberLong" => "long",
            "$numberDouble" => "double",
            "$numberDecimal" => "decimal",
            "$binary" => "binData",
            "$regularExpression" => "regex",
            "$timestamp" => "timestamp",
            _ => string.Empty
        };
        return type.Length > 0;
    }

    private static JsonObject ToSchema(SchemaNode node, bool isRoot = false)
    {
        var schema = new JsonObject();
        var types = node.Types.OrderBy(type => type, StringComparer.Ordinal).ToArray();
        if (isRoot && types.Length == 0)
        {
            schema["bsonType"] = "object";
        }
        else if (types.Length == 1)
        {
            schema["bsonType"] = types[0];
        }
        else if (types.Length > 1)
        {
            schema["bsonType"] = new JsonArray(types.Select(value => JsonValue.Create(value)).ToArray());
        }

        if (node.Properties.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var property in node.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                properties[property.Key] = ToSchema(property.Value);
            }

            schema["properties"] = properties;
        }

        if (node.Items is not null && (node.Items.Types.Count > 0 || node.Items.Properties.Count > 0))
        {
            schema["items"] = ToSchema(node.Items);
        }

        return schema;
    }

    private sealed class SchemaNode
    {
        public HashSet<string> Types { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, SchemaNode> Properties { get; } = new(StringComparer.Ordinal);

        public SchemaNode? Items { get; set; }
    }
}
