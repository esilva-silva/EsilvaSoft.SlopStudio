using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>Top-bar update action: checks periodically, downloads on request and reports through the global operation bar.</summary>
public sealed partial class AppUpdateViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private readonly IAppUpdateService? _updates;
    private readonly IApplicationOperationService _operations;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Timer? _schedule;
    private bool _disposed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsVisible), nameof(Label))] private AppUpdateUiState _state;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private double _progress;
    [ObservableProperty] private string _toolTip = "";

    public AppUpdateViewModel(IAppUpdateService? updates, IApplicationOperationService operations)
    {
        _updates = updates;
        _operations = operations;
        if (updates is null || updates.Availability == AppUpdateAvailability.Disabled) return;
        if (updates.GetStagedUpdate() is { } staged) SetReady(staged);
        _schedule = new Timer(_ => Dispatch(() => _ = CheckAsync()), null, FirstCheckDelay, CheckInterval);
    }

    public AppUpdateRelease? Release { get; private set; }
    public AppVersion? ReadyVersion { get; private set; }
    public bool IsVisible => State != AppUpdateUiState.Hidden;
    public string Label => State switch
    {
        AppUpdateUiState.Downloading => $"Baixando {Progress:F0}%",
        AppUpdateUiState.Ready => "Reiniciar",
        _ => "Atualizar"
    };

    /// <summary>Queries the release feed; failures stay silent because the next scheduled check retries.</summary>
    public async Task CheckAsync()
    {
        if (_disposed || _updates is null || _updates.Availability == AppUpdateAvailability.Disabled || State is AppUpdateUiState.Downloading or AppUpdateUiState.Ready) return;
        AppUpdateRelease? release;
        try { release = await _updates.CheckAsync(_lifetime.Token); }
        catch (Exception) { return; }
        if (_disposed || release is null || State is AppUpdateUiState.Downloading or AppUpdateUiState.Ready) return;
        Release = release;
        if (_updates.Availability == AppUpdateAvailability.ManualOnly)
        {
            State = AppUpdateUiState.ManualOnly;
            ToolTip = $"Versão {release.Version} disponível. A pasta do programa não permite escrita: clique para abrir a página do release.";
            return;
        }
        State = AppUpdateUiState.Available;
        ToolTip = $"Versão {release.Version} disponível (atual {_updates.CurrentVersion}). Clique para baixar; a instalação ocorre ao fechar.";
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (_disposed || _updates is null || Release is not { } release || State != AppUpdateUiState.Available) return;
        State = AppUpdateUiState.Downloading;
        Progress = 0;
        ToolTip = $"Baixando a versão {release.Version}. Use Cancelar na barra de status para interromper.";
        using var operation = _operations.Begin($"Baixando atualização {release.Version}", ApplicationOperationPriority.Normal, canCancel: true, _lifetime.Token);
        void OnProgress(object? sender, EventArgs args) =>
            Dispatch(() => { if (State == AppUpdateUiState.Downloading && operation.Snapshot.Progress is { } value) Progress = value; });
        _operations.Changed += OnProgress;
        try
        {
            // Hashing and extraction of a large package stay off the UI thread.
            var staged = await Task.Run(() => _updates.DownloadAsync(release, operation));
            operation.Complete(ApplicationOperationStatus.Success, $"Atualização {release.Version} pronta; será instalada ao fechar");
            if (!_disposed) SetReady(staged);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            operation.Complete(ApplicationOperationStatus.Cancelled, "Download da atualização cancelado");
            Restore(release, "Download cancelado.");
        }
        catch (Exception ex)
        {
            operation.Complete(ApplicationOperationStatus.Error, "Atualização não baixada: " + ex.Message);
            Restore(release, $"Não foi possível baixar a versão {release.Version}: {ex.Message}");
        }
        finally { _operations.Changed -= OnProgress; }
    }

    private void SetReady(StagedAppUpdate staged)
    {
        ReadyVersion = staged.Version;
        State = AppUpdateUiState.Ready;
        Progress = 100;
        ToolTip = staged.LastApplyError is { } error
            ? $"A atualização {staged.Version} não foi instalada no último fechamento: {error} Uma nova tentativa ocorre ao fechar."
            : $"Atualização {staged.Version} pronta. Será instalada ao fechar o Slop Studio; clique para reiniciar agora.";
    }

    private void Restore(AppUpdateRelease release, string reason)
    {
        if (_disposed) return;
        State = AppUpdateUiState.Available;
        Progress = 0;
        ToolTip = $"{reason} Versão {release.Version} disponível; clique para tentar novamente.";
    }

    private void Dispatch(Action action)
    {
        if (_disposed) return;
        if (_context is null) action(); else _context.Post(_ => { if (!_disposed) action(); }, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _schedule?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
