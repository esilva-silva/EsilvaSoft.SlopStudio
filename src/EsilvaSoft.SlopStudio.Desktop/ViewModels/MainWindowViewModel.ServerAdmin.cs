using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKillOperation))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    private string _operationIdToKill = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKillOperation))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    private string _operationKillConfirmation = string.Empty;

    [ObservableProperty]
    private string _administrationResults = "Carregue o status do servidor ou as estatísticas do banco selecionado.";

    public bool CanKillOperation => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && string.Equals(OperationIdToKill.Trim(), OperationKillConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadServerStatusAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetServerStatusAsync(SelectedProfile, cancellationToken);
            StatusMessage = "Status do servidor carregado.";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadCurrentOperationsAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetCurrentOperationsAsync(SelectedProfile, cancellationToken);
            StatusMessage = "Operações correntes carregadas.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanLoadDatabaseStats))]
    private async Task LoadProfilerStatusAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetProfilerStatusAsync(SelectedProfile, SelectedDatabase, cancellationToken);
            StatusMessage = "Configuração atual do profiler carregada.";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadTopologyAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetTopologyAsync(SelectedProfile, cancellationToken);
            StatusMessage = "Topologia MongoDB carregada.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanKillOperation))]
    private async Task KillOperationAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var request = new OperationKillRequest(OperationIdToKill, OperationKillConfirmation);
        if (!await RunAsync(cancellationToken => _workspace.KillOperationAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        var operationId = request.GetOperationId();
        OperationIdToKill = string.Empty;
        OperationKillConfirmation = string.Empty;
        await RecordAuditAsync("operation.kill", SelectedProfile, "admin", null, $"Interrupção solicitada para a operação {operationId}.");
        StatusMessage = $"Interrupção solicitada para a operação {operationId}.";
    }
}
