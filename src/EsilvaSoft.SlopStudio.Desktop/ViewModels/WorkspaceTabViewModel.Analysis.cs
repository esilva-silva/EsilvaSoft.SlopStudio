using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    [RelayCommand(CanExecute = nameof(CanExplainAggregation))]
    private async Task ExplainAggregationAsync()
    {
        if (!CanExplainAggregation) return;
        var profile = Profile!;
        var query = new AggregationQuery(Database, Collection, Text, Limit);
        using var operation = Operations.Begin($"Analisando pipeline em {profile.Name} › {query.Database} › {query.Collection}", ApplicationOperationPriority.High);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _cancellation = cancellation;
        IsRunning = true; Status = "Analisando pipeline…"; Errors = "";
        try
        {
            var plan = await _workspace.ExplainAggregationAsync(profile, query, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Messages = $"Plano do pipeline capturado — {profile.Name} › {query.Database} › {query.Collection}\n" +
                "queryPlanner: plano estimado; não mede a duração nem a quantidade real de documentos.\n\n" + ExtendedJsonFormatter.Format(plan);
            Status = "Plano disponível"; ResultTabIndex = 1;
            operation.Complete();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = "Cancelado"; Messages = "Análise cancelada. Efeitos já enviados ao servidor não são revertidos."; ResultTabIndex = 1;
            operation.Complete(ApplicationOperationStatus.Cancelled);
        }
        catch (Exception ex)
        {
            Status = "Falha na análise"; Errors = OperationErrorMessages.Describe(ex); ResultTabIndex = 2;
            operation.Complete(ApplicationOperationStatus.Error);
        }
        finally { _cancellation = null; IsRunning = false; ApplyPendingUuidPolicy(); }
    }
}
