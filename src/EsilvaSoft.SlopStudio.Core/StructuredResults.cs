using System.Globalization;
using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Core;

/// <summary>How completely a result represents stored documents; only complete collection reads allow an editable copy.</summary>
public enum ResultCompleteness { Complete, PartialProjection, Derived, Unknown }

/// <summary>Origin captured with the execution. <paramref name="Profile"/> is an in-memory snapshot and never enters session snapshots.</summary>
public sealed record ResultOrigin(string Source, Guid? ProfileId, ConnectionProfile? Profile, string? Database, string? Collection)
{
    public static ResultOrigin Unknown { get; } = new("Resultado", null, null, null, null);

    public bool HasCollection => Profile is not null && !string.IsNullOrWhiteSpace(Database) && !string.IsNullOrWhiteSpace(Collection);

    public string Destination => (Profile?.Name ?? "Conexão desconhecida") + " › " + (string.IsNullOrWhiteSpace(Database) ? "banco desconhecido" : Database)
        + (string.IsNullOrWhiteSpace(Collection) ? "" : " › " + Collection);
}

/// <summary>One result set of an execution: Console expression, query page, aggregation page or script output.</summary>
public sealed class StructuredResultSet
{
    private StructuredResultSet(int number, ResultOrigin origin, string json, bool isTruncated, ResultCompleteness completeness, string? method)
    {
        Number = number; Origin = origin; Json = json; IsTruncated = isTruncated; Completeness = completeness; Method = method;
    }

    public int Number { get; }
    public ResultOrigin Origin { get; }
    /// <summary>JSON of the whole value as returned; used for results that are not documents (counts, write acknowledgements).</summary>
    public string Json { get; }
    public bool IsTruncated { get; }
    public ResultCompleteness Completeness { get; }
    public string? Method { get; }
    /// <summary>Null when the value is not a list of documents.</summary>
    public IReadOnlyList<StructuredResultDocument>? Documents { get; private set; }

    public string Label => "[" + Number.ToString(CultureInfo.InvariantCulture) + "] " + (Origin.Profile is null && Origin.Database is null ? "valor" : Origin.Destination);

    public static StructuredResultSet FromConsole(ConsoleResultSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var completeness = set.Method switch
        {
            "find" or "findOne" => set.IsProjected ? ResultCompleteness.PartialProjection : ResultCompleteness.Complete,
            "aggregate" => ResultCompleteness.Derived,
            _ => ResultCompleteness.Unknown
        };
        var origin = new ResultOrigin("Console · expressão [" + set.Number.ToString(CultureInfo.InvariantCulture) + "]" + (set.Method is null ? "" : " · " + set.Method),
            set.ProfileId, set.SourceProfile, set.Database, string.IsNullOrEmpty(set.Collection) ? null : set.Collection);
        var result = new StructuredResultSet(set.Number, origin, set.Json, set.IsTruncated, completeness, set.Method);
        if (set.Documents is { } documents) result.Documents = documents.Select((json, index) => new StructuredResultDocument(result, index, json)).ToArray();
        return result;
    }

    public static StructuredResultSet FromDocuments(int number, ResultOrigin origin, IReadOnlyList<string> documents, bool isTruncated, ResultCompleteness completeness, string? method = null)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);
        var result = new StructuredResultSet(number, origin, "", isTruncated, completeness, method);
        result.Documents = documents.Select((json, index) => new StructuredResultDocument(result, index, json)).ToArray();
        return result;
    }
}

/// <summary>A returned document with its position and origin. <see cref="Json"/> is the original Extended JSON, never re-serialized.</summary>
public sealed class StructuredResultDocument
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = ExtendedJsonFormatter.MaxDepth };

    internal StructuredResultDocument(StructuredResultSet set, int position, string json)
    {
        Set = set; Position = position; Json = json;
        try
        {
            using var document = JsonDocument.Parse(json, Options);
            Root = document.RootElement.Clone();
            if (Root.Value.ValueKind == JsonValueKind.Object && Root.Value.TryGetProperty("_id", out var id)) IdJson = id.GetRawText();
        }
        catch (JsonException exception)
        {
            InvalidJsonMessage = exception.Message;
        }
    }

    public StructuredResultSet Set { get; }
    /// <summary>Zero-based position inside <see cref="Set"/>.</summary>
    public int Position { get; }
    public string Json { get; }
    public JsonElement? Root { get; }
    /// <summary>Raw Extended JSON of <c>_id</c>, when present.</summary>
    public string? IdJson { get; }
    public string? InvalidJsonMessage { get; }
    public bool IsValid => InvalidJsonMessage is null;
    public string? IdentityFilter => IdJson is null ? null : "{\"_id\":" + IdJson + "}";
    public bool IsTruncated => Set.IsTruncated;
    public bool IsPartialProjection => Set.Completeness == ResultCompleteness.PartialProjection;

    /// <summary>Builds a document without known origin, for callers that only hold its JSON.</summary>
    public static StructuredResultDocument Detached(string json, int position = 0)
    {
        var set = StructuredResultSet.FromDocuments(1, ResultOrigin.Unknown, [json], false, ResultCompleteness.Unknown);
        return position == 0 ? set.Documents![0] : new StructuredResultDocument(set, position, json);
    }
}
