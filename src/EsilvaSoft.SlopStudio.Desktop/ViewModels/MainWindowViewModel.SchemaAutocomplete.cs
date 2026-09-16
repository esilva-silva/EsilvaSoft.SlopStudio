using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private decimal? _schemaSampleMaximumDocuments = 200;

    [ObservableProperty]
    private string _autocompleteSuggestions = "Digite um campo ou operador no filtro e selecione Sugerir.";

    public ObservableCollection<MqlSuggestion> QuerySuggestions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyQuerySuggestionCommand))]
    private MqlSuggestion? _selectedQuerySuggestion;

    [RelayCommand]
    private void SuggestMql()
    {
        QuerySuggestions.Clear();
        SelectedQuerySuggestion = null;
        AutocompleteSuggestions = "As sugestões contextuais ficam no editor: use Ctrl+. ou Ctrl+Espaço.";
    }

    private bool CanApplyQuerySuggestion() => SelectedQuerySuggestion is not null;

    [RelayCommand(CanExecute = nameof(CanApplyQuerySuggestion))]
    private void ApplyQuerySuggestion()
    {
        StatusMessage = "Aplique sugestões diretamente no editor com Ctrl+. ou Ctrl+Espaço.";
    }

    [RelayCommand]
    private void SuggestAggregation()
    {
        AutocompleteSuggestions = "As sugestões de agregação ficam no editor: use Ctrl+. ou Ctrl+Espaço.";
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task SampleSchemaFieldsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var maximum = decimal.ToInt32(SchemaSampleMaximumDocuments ?? 200);
            var sample = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, Limit: maximum, MaxTimeMs: 2_000), cancellationToken);
            _knownFields.Clear();
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(sample.Documents));
            AutocompleteSuggestions = _knownFields.Count == 0
                ? "A amostra não contém campos que possam ser sugeridos."
                : $"{_knownFields.Count} campo(s) inferido(s) de {sample.Documents.Count} documento(s). Use Sugerir MQL no filtro.";
            StatusMessage = $"Amostra de schema carregada: {sample.Documents.Count} documento(s), {_knownFields.Count} campo(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task InferCollectionValidatorAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var maximum = decimal.ToInt32(SchemaSampleMaximumDocuments ?? 200);
            var sample = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, Limit: maximum, MaxTimeMs: 2_000), cancellationToken);
            CollectionValidatorJson = MqlAutocompleteService.InferJsonSchema(sample.Documents);
            StatusMessage = $"Validador inferido de {sample.Documents.Count} documento(s); revise antes de aplicar.";
        });
    }
}
