using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<string> _lastQueryDocuments = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    private string _queryFilter = "{}";

    [ObservableProperty]
    private string _queryProjection = string.Empty;

    [ObservableProperty]
    private string _querySort = string.Empty;

    [ObservableProperty]
    private decimal? _queryLimit = 100;

    [ObservableProperty]
    private decimal? _querySkip;

    [ObservableProperty]
    private string _queryHint = string.Empty;

    [ObservableProperty]
    private string _queryComment = string.Empty;

    [ObservableProperty]
    private decimal? _queryBatchSize;

    [ObservableProperty]
    private string _queryCollation = string.Empty;

    [ObservableProperty]
    private decimal? _queryMaxTimeMs;

    [ObservableProperty]
    private string _queryResults = "Selecione uma conexão, carregue os bancos e execute uma consulta.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportQueryResults))]
    [NotifyCanExecuteChangedFor(nameof(ExportQueryResultsCommand))]
    private string _queryExportPath = string.Empty;

    [ObservableProperty]
    private string _countResults = "A contagem exata respeita o filtro; a estimada considera a coleção inteira.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGetDistinctValues))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    private string _distinctField = string.Empty;

    [ObservableProperty]
    private decimal? _distinctMaximumValues = 1_000;

    [ObservableProperty]
    private string _distinctResults = "Informe um campo e execute valores distintos usando o filtro atual.";

    [ObservableProperty]
    private string _explainResults = "Execute Explain para visualizar o plano e as estatísticas da consulta.";

    [ObservableProperty]
    private string _aggregationPipeline = "[\n  { \"$match\": {} },\n  { \"$limit\": 100 }\n]";

    [ObservableProperty]
    private decimal? _aggregationLimit = 100;

    [ObservableProperty]
    private string _aggregationResults = "Selecione uma coleção e execute um pipeline de agregação.";

    public bool CanExportQueryResults => _lastQueryDocuments.Count > 0 && !string.IsNullOrWhiteSpace(QueryExportPath);

    public bool CanGetDistinctValues => CanExecuteQuery && !string.IsNullOrWhiteSpace(DistinctField);

    partial void OnQueryFilterChanged(string value)
    {
        ExecuteQueryCommand.NotifyCanExecuteChanged();
        QuerySuggestions.Clear();
        SelectedQuerySuggestion = null;
    }

    [RelayCommand]
    private void ShowAllDocuments()
    {
        QueryFilter = "{}";
        QuerySkip = 0;
        StatusMessage = "Filtro definido como {}. A próxima consulta começa no primeiro documento.";
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExecuteQueryAsync()
    {
        var profile = SelectedProfile;
        if (profile is null || string.IsNullOrWhiteSpace(SelectedDatabase) || string.IsNullOrWhiteSpace(SelectedCollection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new MongoQuery(
                SelectedDatabase,
                SelectedCollection,
                QueryFilter,
                ProjectionJson: string.IsNullOrWhiteSpace(QueryProjection) ? null : QueryProjection,
                SortJson: string.IsNullOrWhiteSpace(QuerySort) ? null : QuerySort,
                Limit: decimal.ToInt32(QueryLimit ?? 100),
                Skip: decimal.ToInt32(QuerySkip ?? 0),
                HintJson: string.IsNullOrWhiteSpace(QueryHint) ? null : QueryHint,
                MaxTimeMs: QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value),
                Comment: string.IsNullOrWhiteSpace(QueryComment) ? null : QueryComment,
                BatchSize: QueryBatchSize is null ? null : decimal.ToInt32(QueryBatchSize.Value),
                CollationJson: string.IsNullOrWhiteSpace(QueryCollation) ? null : QueryCollation);
            var result = await _workspace.QueryAsync(profile, query, cancellationToken);
            QueryResults = result.Documents.Count == 0
                ? "Nenhum documento encontrado."
                : string.Join(Environment.NewLine + Environment.NewLine, result.Documents);
            _lastQueryDocuments = result.Documents;
            ExportQueryResultsCommand.NotifyCanExecuteChanged();
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(result.Documents));
            if (QueryHistoryEnabled)
            {
                var historyEntry = QueryHistoryEntry.Create(profile.Id, query);
                await _workspace.SaveQueryHistoryAsync(historyEntry, cancellationToken);
                QueryHistory.Insert(0, historyEntry);
                while (QueryHistory.Count > 50)
                {
                    QueryHistory.RemoveAt(QueryHistory.Count - 1);
                }
            }
            StatusMessage = $"{result.Documents.Count} documento(s) retornado(s) em {result.Duration.TotalMilliseconds:F0} ms.{(result.IsTruncated ? " Limite atingido." : string.Empty)}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExportQueryResults))]
    private async Task ExportQueryResultsAsync()
    {
        try
        {
            if (!string.Equals(Path.GetExtension(QueryExportPath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Informe um arquivo novo com extensão .json.", nameof(QueryExportPath));
            }

            var destination = Path.GetFullPath(QueryExportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var content = QueryResultExportSerializer.Serialize(_lastQueryDocuments);
            await using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);
            StatusMessage = $"{_lastQueryDocuments.Count} documento(s) exportado(s) em Extended JSON.";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetError(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private Task CountExactDocumentsAsync() => CountDocumentsAsync(useEstimatedCount: false);

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private Task CountEstimatedDocumentsAsync() => CountDocumentsAsync(useEstimatedCount: true);

    private async Task CountDocumentsAsync(bool useEstimatedCount)
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new CollectionCountRequest(
                database,
                collection,
                useEstimatedCount ? "{}" : QueryFilter,
                useEstimatedCount,
                QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value));
            var result = await _workspace.CountDocumentsAsync(profile, request, cancellationToken);
            CountResults = result.IsEstimated
                ? $"Estimativa da coleção: {result.Count:N0} documento(s) em {result.Duration.TotalMilliseconds:F0} ms. Não usa filtro."
                : $"Contagem exata do filtro: {result.Count:N0} documento(s) em {result.Duration.TotalMilliseconds:F0} ms.";
            StatusMessage = result.IsEstimated ? "Estimativa da coleção concluída." : "Contagem exata concluída.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanGetDistinctValues))]
    private async Task GetDistinctValuesAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new DistinctValuesRequest(
                database,
                collection,
                DistinctField,
                QueryFilter,
                decimal.ToInt32(DistinctMaximumValues ?? 1_000),
                QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value));
            var result = await _workspace.GetDistinctValuesAsync(profile, request, cancellationToken);
            DistinctResults = result.Values.Count == 0
                ? "Nenhum valor distinto encontrado."
                : string.Join(Environment.NewLine, result.Values);
            StatusMessage = $"{result.Values.Count} valor(es) distinto(s) retornado(s) em {result.Duration.TotalMilliseconds:F0} ms." +
                (result.IsTruncated ? " Limite atingido." : string.Empty);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExplainQueryAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase) || string.IsNullOrWhiteSpace(SelectedCollection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new MongoQuery(
                SelectedDatabase,
                SelectedCollection,
                QueryFilter,
                ProjectionJson: string.IsNullOrWhiteSpace(QueryProjection) ? null : QueryProjection,
                SortJson: string.IsNullOrWhiteSpace(QuerySort) ? null : QuerySort,
                Limit: decimal.ToInt32(QueryLimit ?? 100),
                Skip: decimal.ToInt32(QuerySkip ?? 0),
                HintJson: string.IsNullOrWhiteSpace(QueryHint) ? null : QueryHint,
                MaxTimeMs: QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value),
                Comment: string.IsNullOrWhiteSpace(QueryComment) ? null : QueryComment,
                BatchSize: QueryBatchSize is null ? null : decimal.ToInt32(QueryBatchSize.Value),
                CollationJson: string.IsNullOrWhiteSpace(QueryCollation) ? null : QueryCollation);
            ExplainResults = await _workspace.ExplainAsync(SelectedProfile, query, cancellationToken);
            StatusMessage = "Plano Explain carregado.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExecuteAggregationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new AggregationQuery(
                database,
                collection,
                AggregationPipeline,
                decimal.ToInt32(AggregationLimit ?? 100));
            var result = await _workspace.AggregateAsync(profile, query, cancellationToken);
            AggregationResults = result.Documents.Count == 0
                ? "O pipeline não retornou documentos."
                : string.Join(Environment.NewLine + Environment.NewLine, result.Documents);
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(result.Documents));
            StatusMessage = $"Pipeline retornou {result.Documents.Count} documento(s) em {result.Duration.TotalMilliseconds:F0} ms.{(result.IsTruncated ? " Limite atingido." : string.Empty)}";
        });
    }
}
