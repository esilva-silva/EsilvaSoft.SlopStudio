using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class EnvironmentsViewModel : ObservableObject
{
    private readonly WorkspaceService _workspace;
    private bool _loaded;
    public ObservableCollection<EnvironmentDefinition> Environments { get; } = [];
    public ObservableCollection<string> Keys { get; } = [];
    [ObservableProperty] private EnvironmentDefinition? _selectedEnvironment;
    [ObservableProperty] private string? _selectedKey;
    [ObservableProperty] private string _environmentName = "";
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _status = "";
    public event EventHandler? Saved;

    public EnvironmentsViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
        try
        {
            var vault = workspace.LoadEnvironments();
            foreach (var environment in vault.Environments) Environments.Add(environment);
            SelectedEnvironment = Environments.Single(e => e.Name == vault.ActiveEnvironment);
            Status = "Ambiente ativo: " + vault.ActiveEnvironment;
            _loaded = true;
        }
        catch (Exception ex) { Status = "Não foi possível carregar os ambientes: " + ex.Message; }
    }

    partial void OnSelectedEnvironmentChanged(EnvironmentDefinition? value)
    {
        Keys.Clear();
        if (value is not null) foreach (var key in value.Values.Keys.Order()) Keys.Add(key);
        Key = ""; Value = ""; SelectedKey = null;
    }
    partial void OnSelectedKeyChanged(string? value)
    {
        Key = value ?? "";
        Value = value is not null && SelectedEnvironment is not null ? SelectedEnvironment.Values[value] : "";
    }

    [RelayCommand]
    private void AddEnvironment()
    {
        var name = EnvironmentName.Trim();
        if (!_loaded || name.Length is 0 or > 60 || Environments.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        { Status = "Informe um nome único de ambiente, com até 60 caracteres."; return; }
        var environment = new EnvironmentDefinition(name, []);
        Environments.Add(environment); SelectedEnvironment = environment; EnvironmentName = "";
        Status = "Ambiente adicionado ao formulário. Salve para aplicar.";
    }

    [RelayCommand]
    private void SetValue()
    {
        if (SelectedEnvironment is null || string.IsNullOrWhiteSpace(Key)) { Status = "Selecione um ambiente e informe uma chave."; return; }
        var key = Key.Trim();
        SelectedEnvironment.Values[key] = Value;
        if (!Keys.Contains(key)) Keys.Add(key);
        Key = ""; Value = ""; SelectedKey = null;
        Status = "Variável atualizada no formulário. Salve para aplicar.";
    }

    [RelayCommand]
    private void RemoveValue()
    {
        if (SelectedEnvironment is null || SelectedKey is null) return;
        var key = SelectedKey;
        SelectedEnvironment.Values.Remove(key); Keys.Remove(key);
        Key = ""; Value = ""; SelectedKey = null;
        Status = "Variável removida do formulário. Salve para aplicar.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_loaded || SelectedEnvironment is null) return;
        if (!string.IsNullOrEmpty(Key) || !string.IsNullOrEmpty(Value))
        { Status = "Adicione a variável em edição antes de salvar."; return; }
        try
        {
            var selectedName = SelectedEnvironment.Name;
            var snapshot = new EnvironmentVault(1, selectedName, Environments.Select(environment => new EnvironmentDefinition(environment.Name, new(environment.Values))).ToArray());
            await _workspace.SaveEnvironmentsAsync(snapshot);
            Status = "Ambiente ativo: " + selectedName + ". Reabra as conexões para atualizar o destino.";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Status = "Ambientes não salvos: " + ex.Message; }
    }
}
