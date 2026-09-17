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
            Access = context.CatalogAccess
        };
        var result = _catalog.Query(query, cancellationToken);
        var items = result.Candidates.Select(candidate => ToItem(candidate, context))
            .Concat(context.LocalSymbols.Select(symbol => ToLocalItem(symbol, context)))
            .ToArray();
        var ranked = _ranker.Rank(items, context.Prefix, context.MaximumItems, cancellationToken);
        return ValueTask.FromResult(new CompletionList(context.Version, ranked, result.Completeness != CatalogCompleteness.Complete));
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
