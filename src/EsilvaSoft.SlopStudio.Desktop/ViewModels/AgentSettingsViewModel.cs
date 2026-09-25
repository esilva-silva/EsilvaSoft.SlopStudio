using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>
/// Provider configuration driven by capabilities. Opening it only lists the catalog: it never starts authentication or
/// touches the vault. Only officially supported methods appear (no subscription login). Storing a key is an explicit
/// action and never consents to sending data; the key is not echoed in status, logs or bindings after saving.
/// </summary>
public sealed partial class AgentSettingsViewModel : ObservableObject
{
    private readonly IAgentProviderCatalog? _catalog;
    private readonly IAgentApiKeyStore? _credentials;

    public AgentSettingsViewModel(IAgentProviderCatalog? catalog, IAgentApiKeyStore? credentials, string? providerId = null)
    {
        _catalog = catalog;
        _credentials = credentials;
        Reload(providerId);
    }

    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public ObservableCollection<AgentProviderOption> Providers { get; } = [];

    public bool HasProviders => Providers.Count > 0;

    public bool CanSetUpCredentials => _credentials is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiresApiKey), nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(SaveApiKeyCommand), nameof(RemoveApiKeyCommand))]
    private AgentProviderOption? _selectedProvider;

    /// <summary>Bound to a masked input. Cleared as soon as a save starts.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveApiKeyCommand))]
    private string _apiKey = "";

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private bool _isStatusError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveApiKeyCommand), nameof(RemoveApiKeyCommand))]
    private bool _isBusy;

    public bool HasSelection => SelectedProvider is not null;

    public bool RequiresApiKey => SelectedProvider?.RequiresApiKey == true;

    public string CredentialSetupNote => Text.Resolve(CanSetUpCredentials ? "agentSettingsVaultNote" : "agentKeySetupUnavailable");

    private bool _reloading;

    private void Reload(string? providerId)
    {
        _reloading = true;
        try
        {
            ReloadCore(providerId);
        }
        finally
        {
            _reloading = false;
        }
    }

    private void ReloadCore(string? providerId)
    {
        Providers.Clear();
        IReadOnlyList<AgentProviderPresentation> listed;
        try
        {
            listed = _catalog?.List() ?? [];
        }
        catch (Exception)
        {
            listed = [];
        }

        foreach (var item in listed)
        {
            Providers.Add(new AgentProviderOption(item));
        }

        SelectedProvider = Providers.FirstOrDefault(p => p.ProviderId == providerId) ?? Providers.FirstOrDefault();
        OnPropertyChanged(nameof(HasProviders));
    }

    partial void OnSelectedProviderChanged(AgentProviderOption? oldValue, AgentProviderOption? newValue)
    {
        if (_reloading || oldValue?.ProviderId == newValue?.ProviderId)
        {
            return; // Reload of the same provider keeps the last report.
        }

        ApiKey = "";
        StatusText = "";
        IsStatusError = false;
    }

    private bool CanSaveApiKey() => !IsBusy && _credentials is not null && RequiresApiKey && ApiKey.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSaveApiKey))]
    private async Task SaveApiKeyAsync()
    {
        var providerId = SelectedProvider!.ProviderId;
        var buffer = ApiKey.ToCharArray();
        ApiKey = "";
        IsBusy = true;
        try
        {
            var outcome = await _credentials!.SaveApiKeyAsync(providerId, buffer, CancellationToken.None);
            Report(outcome);
        }
        catch (Exception)
        {
            // Exception text could echo the secret or vault internals; only a fixed message is shown.
            Report(AgentCredentialSetupOutcome.Failed);
        }
        finally
        {
            Array.Clear(buffer);
            IsBusy = false;
            Reload(providerId);
        }
    }

    private bool CanRemoveApiKey() => !IsBusy && _credentials is not null && RequiresApiKey;

    [RelayCommand(CanExecute = nameof(CanRemoveApiKey))]
    private async Task RemoveApiKeyAsync()
    {
        var providerId = SelectedProvider!.ProviderId;
        IsBusy = true;
        try
        {
            Report(await _credentials!.RemoveApiKeyAsync(providerId, CancellationToken.None));
        }
        catch (Exception)
        {
            Report(AgentCredentialSetupOutcome.Failed);
        }
        finally
        {
            IsBusy = false;
            Reload(providerId);
        }
    }

    private void Report(AgentCredentialSetupOutcome outcome)
    {
        var (key, error) = outcome switch
        {
            AgentCredentialSetupOutcome.Saved => ("agentKeySaved", false),
            AgentCredentialSetupOutcome.Removed => ("agentKeyRemoved", false),
            AgentCredentialSetupOutcome.VaultUnavailable => ("agentKeyVaultUnavailable", true),
            AgentCredentialSetupOutcome.Cancelled => ("agentKeyCancelled", false),
            AgentCredentialSetupOutcome.Rejected => ("agentKeyRejected", true),
            _ => ("agentKeyFailed", true),
        };
        StatusText = Text.Resolve(key);
        IsStatusError = error;
    }
}
