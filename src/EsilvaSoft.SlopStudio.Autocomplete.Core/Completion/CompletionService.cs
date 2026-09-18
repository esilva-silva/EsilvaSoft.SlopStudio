using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

/// <summary>Deterministic catalog orchestration. It never asks a source for kinds the context did not request.</summary>
public sealed class CompletionService
{
    private readonly IKnowledgeCatalog _catalog;
    private readonly CompletionRanker _ranker;
    private readonly ICompletionProfileResolver? _profiles;

    public CompletionService(IKnowledgeCatalog catalog, CompletionRanker? ranker = null, ICompletionProfileResolver? profiles = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _ranker = ranker ?? new CompletionRanker();
        _profiles = profiles;
    }

    public ValueTask<CompletionList> CompleteAsync(CompletionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var query = new CatalogQuery(context.ExpectedKinds, context.Dialect, context.Prefix)
        {
            Connection = context.Scope is { } scope ? _profiles?.Resolve(scope.Connection.ProfileId) : null,
            Database = context.Scope?.Database ?? "",
            Collection = context.Scope?.Collection ?? "",
            ParentPath = context.ParentPath,
            LocalSchemas = context.LocalSchemas,
            LocalSymbols = context.LocalSymbols,
            MaximumCandidates = context.MaximumCandidates,
            Access = context.CatalogAccess,
            RestrictFieldsToLocalSchemas = context.RestrictFieldsToLocalSchemas
        };
        var result = _catalog.Query(query, cancellationToken);
        var items = result.Candidates.Select(candidate => ToItem(candidate, context))
            .Concat(context.LocalSymbols.Select(symbol => ToLocalItem(symbol, context)))
            .ToArray();
        // Passa o contexto, e não só o prefixo: é ele que identifica conexão, banco, coleção e forma para o termo
        // de uso recente. Sem rastreador de uso configurado o resultado é idêntico ao do ranking por texto.
        var ranked = _ranker.Rank(items, context, context.MaximumItems, cancellationToken);
        // Truncamento tem três origens e todas precisam aparecer em IsIncomplete, senão um consumidor que exige
        // "o conjunto inteiro foi visto" (o portão de confiança do ghost) concluiria unicidade a partir de uma
        // amostra. (1) Completeness da fonte, para metadados parciais/carregando. (2) Corte por MaximumCandidates:
        // o catálogo para de percorrer grupos quando a cota enche e, com fonte única, isso não vira Partial — o
        // sinal aqui é a cota ter sido atingida. (3) Corte por MaximumItems: a página voltou cheia e ainda havia
        // candidatos não devolvidos, então o que ficou de fora pode conter justamente o desempate.
        var truncatedByCandidates = result.Candidates.Count >= query.MaximumCandidates;
        var truncatedByItems = ranked.Count >= context.MaximumItems && ranked.Count < items.Length;
        return ValueTask.FromResult(new CompletionList(context.Version, ranked,
            result.Completeness != CatalogCompleteness.Complete || truncatedByCandidates || truncatedByItems));
    }

    private static CompletionItem ToItem(CatalogCandidate candidate, CompletionContext context)
    {
        var symbol = candidate.Symbol;
        var kind = symbol.Kind switch
        {
            SymbolKind.Field => CompletionItemKind.Field,
            SymbolKind.ConnectionMethod or SymbolKind.DatabaseMethod or SymbolKind.CollectionMethod or SymbolKind.CursorMethod => CompletionItemKind.Method,
            SymbolKind.GlobalFunction or SymbolKind.BsonConstructor => CompletionItemKind.Function,
            SymbolKind.Keyword => CompletionItemKind.Keyword,
            SymbolKind.QueryOperator or SymbolKind.UpdateOperator or SymbolKind.ExpressionOperator or SymbolKind.AggregationStage => CompletionItemKind.Operator,
            SymbolKind.Snippet => CompletionItemKind.Snippet,
            _ => CompletionItemKind.Value
        };
        var tags = CompletionItemTags.None;
        if (symbol.Flags.HasFlag(SymbolTraits.Write)) tags |= CompletionItemTags.Write;
        if (symbol.Flags.HasFlag(SymbolTraits.Deprecated)) tags |= CompletionItemTags.Deprecated;
        if (symbol.Flags.HasFlag(SymbolTraits.Stale)) tags |= CompletionItemTags.Stale;
        var text = symbol.Snippet ?? symbol.Name;
        return new(symbol.Id, symbol.Name, symbol.Detail, kind,
            new(context.ReplaceSpan, context.ReplaceSpan, text, symbol.Snippet is not null), symbol.Name, 0,
            symbol.Kind == SymbolKind.Snippet ? CompletionSource.Snippet :
            symbol.Evidence != EvidenceSources.None ? CompletionSource.Schema : CompletionSource.Catalog)
        {
            Tags = tags,
            CatalogKind = symbol.Kind,
            Documentation = new(symbol.Category, symbol.ValueShape, symbol.Parameters, symbol.Returns, symbol.Since, symbol.Evidence)
        };
    }

    private static CompletionItem ToLocalItem(CatalogSymbol symbol, CompletionContext context) => new(
        symbol.Id, symbol.Name, symbol.Detail, CompletionItemKind.Variable,
        new(context.ReplaceSpan, context.ReplaceSpan, symbol.Name), symbol.Name, 0, CompletionSource.Local)
    { CatalogKind = symbol.Kind, Documentation = new(symbol.Category, symbol.ValueShape, symbol.Parameters, symbol.Returns, symbol.Since, symbol.Evidence) };
}
