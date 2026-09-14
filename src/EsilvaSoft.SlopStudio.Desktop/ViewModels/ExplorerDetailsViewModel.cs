using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class ExplorerDetailsViewModel(WorkspaceService workspace) : ObservableObject
{
    private CancellationTokenSource? _cancellation;
    private int _generation;
    public ObservableCollection<InstanceInfo> Instances { get; } = [];
    [ObservableProperty] private InstanceInfo? _selectedInstance;
    [ObservableProperty] private string _title = "Detalhes";
    [ObservableProperty] private string _text = "Selecione um item da árvore.";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isConnection;
    public ExplorerNodeViewModel? Node { get; private set; }
    public Task SelectionTask { get; private set; } = Task.CompletedTask;

    public Task SelectAsync(ExplorerNodeViewModel? node) => SelectionTask = SelectCoreAsync(node);
    public Task SelectServerAsync(ExplorerNodeViewModel node) => SelectionTask = SelectCoreAsync(node, true);
    private async Task SelectCoreAsync(ExplorerNodeViewModel? node, bool serverStatus = false)
    {
        Clear(); Node = node;
        if (node is null) return;
        var generation = _generation;
        Title = node.Name;
        IsConnection = node.IsConnection;
        Text = node.Context + (node.IsConnection ? "\nServidor(es): " + node.Profile.Endpoint : "");
        if (node.IsIndex)
        {
            var index = node.Index!;
            Text += $"\nNome: {index.Name}\nCampos / direção:\n{index.Keys}\nUnique: {(index.Unique ? "Sim" : "Não")}\nSparse: {(index.Sparse ? "Sim" : "Não")}\nTTL: {index.Ttl}\nFiltro parcial: {index.PartialFilter}\n\nDefinição completa:\n{index.Definition}";
            return;
        }
        if (!node.Root.IsConnected) { Text += "\nDesconectada. Conecte para carregar metadados."; return; }
        IsLoading = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        try
        {
            string details;
            TopologyInfo? topology = null;
            if (node.IsConnection && serverStatus) details = await workspace.GetServerStatusAsync(node.Profile, cancellation.Token);
            else if (node.IsConnection)
            {
                topology = await workspace.GetExplorerTopologyAsync(node.Profile, cancellation.Token);
                details = $"Servidor(es): {node.Profile.Endpoint}\nBanco padrão: {node.Profile.DefaultDatabase ?? "não definido"}\nSomente leitura: {node.Profile.IsReadOnly}\n{topology.Kind} · {topology.ReplicaSet ?? "sem replica set"} · {topology.Server}\n{topology.Definition}";
            }
            else if (node.IsDatabase) details = await workspace.GetDatabaseStatsAsync(node.Profile, node.Database, cancellation.Token);
            else if (node.HasCollection) details = await workspace.GetExplorerCollectionDetailsAsync(node.Profile, node.Database, node.Collection!, cancellation.Token);
            else details = node.Message;
            if (generation != _generation) return;
            Text = node.Context + "\n\n" + details;
            if (topology is not null) foreach (var instance in topology.Instances) Instances.Add(instance);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (generation == _generation) Text = node.Context + "\nNão foi possível carregar os detalhes: " + OperationErrorMessages.Describe(ex); }
        finally
        {
            if (generation == _generation) { IsLoading = false; _cancellation = null; }
        }
    }

    public void Clear()
    {
        _generation++; _cancellation?.Cancel(); _cancellation = null;
        Instances.Clear(); SelectedInstance = null; IsLoading = false; IsConnection = false;
        Node = null; Title = "Detalhes"; Text = "Selecione um item da árvore.";
    }
}
