using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    public Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> ConfirmConsoleWrite { get; set; } = (_, _) => Task.FromResult(false);
    public ObservableCollection<ConsoleHistoryEntry> ConsoleHistory { get; } = [];
    public ObservableCollection<ConsoleResultSet> ConsoleResults { get; } = [];
    [ObservableProperty] private ConsoleResultSet? _selectedConsoleResult;
    [ObservableProperty] private ConsoleHistoryEntry? _selectedConsoleHistory;
    partial void OnSelectedConsoleResultChanged(ConsoleResultSet? value)
    {
        if (_resultIsConsole) ShowSetDocuments(value is not null && _consoleSets.TryGetValue(value, out var set) ? set : null);
    }
    public bool IsConsole => Mode == "Console";
    public ConsoleStatement GetConsoleStatement(int caret) => _workspace.GetConsoleStatement(Text, caret);
}
