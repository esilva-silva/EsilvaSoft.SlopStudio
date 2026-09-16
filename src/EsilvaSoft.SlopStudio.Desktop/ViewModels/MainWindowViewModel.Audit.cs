using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<AuditEntry> AuditEntries { get; } = [];

    [ObservableProperty]
    private string _auditExportPath = string.Empty;

    [RelayCommand]
    private async Task LoadAuditAsync()
    {
        await RunAsync(async cancellationToken =>
        {
            var entries = await _workspace.GetRecentAuditAsync(cancellationToken: cancellationToken);
            AuditEntries.Clear();
            foreach (var entry in entries)
            {
                AuditEntries.Add(entry);
            }

            AdministrationResults = entries.Count == 0
                ? "Nenhuma ação auditada no workspace local."
                : string.Join(Environment.NewLine, entries.Select(entry => entry.DisplayText));
            StatusMessage = $"{entries.Count} ação(ões) locais de auditoria carregada(s).";
        });
    }

    [RelayCommand]
    private async Task ExportAuditAsync()
    {
        if (string.IsNullOrWhiteSpace(AuditExportPath))
        {
            SetError("Informe o caminho do arquivo JSON da auditoria.");
            return;
        }

        if (!string.Equals(Path.GetExtension(AuditExportPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            SetError("O arquivo de auditoria precisa usar a extensão .json.");
            return;
        }

        try
        {
            var entries = await _workspace.GetRecentAuditAsync(500);
            var json = AuditJsonSerializer.Serialize(entries);
            var directory = Path.GetDirectoryName(Path.GetFullPath(AuditExportPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(
                AuditExportPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            StatusMessage = $"Auditoria exportada para {AuditExportPath}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetError(File.Exists(AuditExportPath)
                ? "O arquivo de auditoria já existe. Escolha outro caminho para não sobrescrever a evidência anterior."
                : $"Falha ao exportar auditoria: {exception.Message}");
        }
    }
}
