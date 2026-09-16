using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<ScriptHistoryEntry> ScriptHistory { get; } = [];

    [ObservableProperty]
    private string _scriptInput = "{}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveScript))]
    [NotifyCanExecuteChangedFor(nameof(SaveScriptCommand))]
    private string _scriptText = "// db começa no banco selecionado da conexão\nconst filtro = EJSON.parse('{ \"ativo\": true }');\nconst cursor = db.getCollection(\"clientes\").find(filtro).limit(100);\nawait slop.results.stream(cursor);";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveScript))]
    [NotifyPropertyChangedFor(nameof(CanLoadScript))]
    [NotifyCanExecuteChangedFor(nameof(SaveScriptCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadScriptCommand))]
    private string _scriptFilePath = string.Empty;

    [ObservableProperty]
    private string _scriptResults = "O resultado estruturado e o console do mongosh aparecem aqui.";

    [ObservableProperty]
    private ScriptHistoryEntry? _selectedScriptHistory;

    [ObservableProperty]
    private bool _scriptHistoryEnabled = true;

    [ObservableProperty]
    private bool _persistScriptInput;

    public bool CanSaveScript => !string.IsNullOrWhiteSpace(ScriptFilePath) && !string.IsNullOrWhiteSpace(ScriptText);

    public bool CanLoadScript => !string.IsNullOrWhiteSpace(ScriptFilePath);

    partial void OnSelectedScriptHistoryChanged(ScriptHistoryEntry? value)
    {
        if (value is not null)
        {
            ScriptFilePath = value.Path;
            if (value.InputJson is not null)
            {
                ScriptInput = value.InputJson;
            }

            StatusMessage = "Caminho de script carregado do histórico local.";
        }
    }

    partial void OnScriptHistoryEnabledChanged(bool value)
    {
        if (!value)
        {
            SelectedScriptHistory = null;
            ScriptHistory.Clear();
            return;
        }

        _ = LoadScriptHistoryAsync();
    }

    [RelayCommand]
    private async Task LoadScriptHistoryAsync()
    {
        if (!ScriptHistoryEnabled)
        {
            ScriptHistory.Clear();
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var entries = await _workspace.GetRecentScriptHistoryAsync(cancellationToken: cancellationToken);
            ScriptHistory.Clear();
            foreach (var entry in entries)
            {
                ScriptHistory.Add(entry);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task ExecuteScriptAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = await _workspace.ExecuteScriptAsync(SelectedProfile, ScriptText, ScriptInput, SelectedDatabase, cancellationToken);
            ScriptResults = BuildScriptOutput(result);
            StatusMessage = result.ExitCode == 0
                ? $"Script concluído em {result.Duration.TotalMilliseconds:F0} ms."
                : $"Script concluído com código {result.ExitCode}.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanSaveScript))]
    private async Task SaveScriptAsync()
    {
        if (!await RunAsync(async cancellationToken =>
        {
            await _workspace.SaveScriptAsync(ScriptFilePath, ScriptText, cancellationToken);
            await RememberScriptAsync(cancellationToken);
        }))
        {
            return;
        }

        StatusMessage = $"Script salvo em {Path.GetFullPath(ScriptFilePath)}.";
    }

    [RelayCommand(CanExecute = nameof(CanLoadScript))]
    private async Task LoadScriptAsync()
    {
        var loaded = string.Empty;
        if (!await RunAsync(async cancellationToken =>
        {
            loaded = await _workspace.LoadScriptAsync(ScriptFilePath, cancellationToken);
            await RememberScriptAsync(cancellationToken);
        }))
        {
            return;
        }

        ScriptText = loaded;
        StatusMessage = $"Script carregado de {Path.GetFullPath(ScriptFilePath)}.";
    }

    private async Task RememberScriptAsync(CancellationToken cancellationToken)
    {
        if (!ScriptHistoryEnabled)
        {
            return;
        }

        var entry = ScriptHistoryEntry.Create(ScriptFilePath, inputJson: PersistScriptInput ? ScriptInput : null);
        await _workspace.SaveScriptHistoryAsync(entry, cancellationToken);
        var existing = ScriptHistory.FirstOrDefault(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ScriptHistory.Remove(existing);
        }

        ScriptHistory.Insert(0, entry);
        while (ScriptHistory.Count > 50)
        {
            ScriptHistory.RemoveAt(ScriptHistory.Count - 1);
        }
    }

    private static string BuildScriptOutput(ScriptExecutionResult result)
    {
        var sections = new List<string>();

        if (result.Results.Count > 0)
        {
            sections.Add("RESULTADOS EJSON" + Environment.NewLine + string.Join(Environment.NewLine, result.Results));
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            sections.Add("CONSOLE" + Environment.NewLine + result.StandardOutput);
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            sections.Add("ERROS" + Environment.NewLine + result.StandardError);
        }

        return sections.Count == 0 ? "O script não retornou resultados nem mensagens." : string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}
