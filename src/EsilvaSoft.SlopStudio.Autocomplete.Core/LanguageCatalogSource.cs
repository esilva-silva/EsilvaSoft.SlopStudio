namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Built-in MongoDB language: one name table per symbol kind.</summary>
public sealed class LanguageCatalogSource : ICatalogSource
{
    private readonly KeyValuePair<SymbolKind, NameTable<CatalogSymbol>>[] _tables;

    public LanguageCatalogSource(LanguageDefinition? language = null)
    {
        var definition = language ?? LanguageDefinition.Default;
        _tables = definition.Symbols.GroupBy(symbol => symbol.Kind).OrderBy(group => group.Key)
            .Select(group => KeyValuePair.Create(group.Key, new NameTable<CatalogSymbol>(group, symbol => symbol.Name, SearchKey))).ToArray();
        ProvidedKinds = _tables.Aggregate(SymbolKinds.None, (all, table) => all | table.Key.ToFlag());
    }

    public SymbolKinds ProvidedKinds { get; }

    public CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sink);
        // Operators match with or without the leading $: "eq" finds $eq.
        var prefix = SearchKey(query.Prefix);
        var dialects = query.Dialect == EditorDialects.None ? EditorDialects.All : query.Dialect | EditorDialects.Mql;
        foreach (var (kind, table) in _tables)
        {
            if ((query.Kinds & kind.ToFlag()) == 0) continue;
            cancellationToken.ThrowIfCancellationRequested();
            table.Collect(prefix, query.MaximumCandidates - sink.Count, symbol => (symbol.Dialects & dialects) != 0, (symbol, match) => sink.Add(new(symbol, match)), cancellationToken);
        }
        return CatalogCompleteness.Complete;
    }

    internal static string SearchKey(string name) => name.TrimStart('$');
}
