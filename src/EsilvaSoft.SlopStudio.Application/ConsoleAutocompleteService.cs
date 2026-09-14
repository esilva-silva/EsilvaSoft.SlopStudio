using System.Text.Json;
using System.Text.RegularExpressions;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public sealed record ConsoleCompletion(string Text, string Description, int Start, int Length);

public sealed class ConsoleAutocompleteService(WorkspaceService workspace, IReadOnlyList<SyntaxHighlighting.SyntaxNamespace>? known = null)
{
    private static readonly Regex Path = new("""(?:^|[^\w])(ConnectionPool|db)((?:\.[\w$]+|\["(?:[^"\\]|\\.)*"\])*)\.(?<partial>[\w$]*)$""", RegexOptions.CultureInvariant);
    private static readonly Regex Segment = new("""\.([\w$]+)|\[("(?:[^"\\]|\\.)*")\]""", RegexOptions.CultureInvariant);
    private static readonly string[] Methods = ["find({})", "findOne({})", "countDocuments({})", "estimatedDocumentCount()", "distinct(\"campo\")", "aggregate([])", "insertOne({})", "insertMany([])", "updateOne({_id: 1}, {$set: {}})", "updateMany({_id: 1}, {$set: {}})", "replaceOne({_id: 1}, {})", "deleteOne({_id: 1})", "deleteMany({_id: 1})", "createIndex({campo: 1})", "dropIndex(\"nome\")"];
    public async Task<IReadOnlyList<ConsoleCompletion>> GetAsync(string prefix, ConnectionProfile? primary, string database, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var match = Path.Match(prefix);
        IEnumerable<string> names;
        var description = "Console JavaScript";
        var partial = Regex.Match(prefix, @"[\w$]*$").Value;
        var start = prefix.Length - partial.Length;
        var property = false;
        if (match.Success)
        {
            property = true;
            var segments = Segment.Matches(match.Groups[2].Value).Select(m => m.Groups[1].Success ? m.Groups[1].Value : JsonSerializer.Deserialize<string>(m.Groups[2].Value)!).ToArray();
            if (match.Groups[1].Value == "db")
            {
                names = segments.Length > 0 ? Methods : primary is null ? ["getCollection(\"colecao\")"] :
                    (known is null ? await workspace.GetCollectionsAsync(primary, database, token).ConfigureAwait(false) : known.Where(n => n.Connection == primary.Name && n.Database == database && n.Collection.Length > 0).Select(n => n.Collection)).Append("getCollection(\"colecao\")");
            }
            else
            {
                if (known is not null)
                {
                    names = segments.Length == 0 ? known.Select(n => n.Connection) : segments.Length == 1
                        ? known.Where(n => n.Connection == segments[0] && n.Database.Length > 0).Select(n => n.Database).Append("getDatabase(\"banco\")")
                        : segments.Length == 2 ? known.Where(n => n.Connection == segments[0] && n.Database == segments[1] && n.Collection.Length > 0).Select(n => n.Collection).Append("getCollection(\"colecao\")") : Methods;
                }
                else
                {
                var profiles = await workspace.GetProfilesAsync(token).ConfigureAwait(false);
                if (segments.Length == 0) names = profiles.Select(p => p.Name);
                else
                {
                    var profile = profiles.SingleOrDefault(p => p.Name == segments[0]);
                    if (profile is null) return [];
                    names = segments.Length == 1 ? (await workspace.GetDatabasesAsync(profile, token).ConfigureAwait(false)).Append("getDatabase(\"banco\")") :
                        segments.Length == 2 ? (await workspace.GetCollectionsAsync(profile, segments[1], token).ConfigureAwait(false)).Append("getCollection(\"colecao\")") : Methods;
                }
                }
            }
        }
        else names = ["db", "getConnection(\"NomeDaConexao\")", "ConnectionPool", "ENV.get(\"chave\")", "console.log()", "ObjectId(\"000000000000000000000000\")", "NumberLong(\"0\")", "NumberDecimal(\"0\")",
            "UUID(\"00000000-0000-0000-0000-000000000000\")", "CGUUID(\"00000000-0000-0000-0000-000000000000\")", "JUUID(\"00000000-0000-0000-0000-000000000000\")", "GUUID(\"00000000-0000-0000-0000-000000000000\")"];
        return names.Where(name => name.StartsWith(partial, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).Take(100).Select(name =>
        {
            var indexer = property && !name.Contains('(') && !Regex.IsMatch(name, @"^[A-Za-z_$][\w$]*$", RegexOptions.CultureInvariant);
            return new ConsoleCompletion(indexer ? "[" + JsonSerializer.Serialize(name) + "]" : name, description,
                indexer ? start - 1 : start, indexer ? partial.Length + 1 : partial.Length);
        }).ToArray();
    }
}
