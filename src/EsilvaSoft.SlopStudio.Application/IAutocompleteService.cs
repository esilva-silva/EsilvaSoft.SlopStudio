using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IAutocompleteService
{
    AutocompleteSettings Settings { get; }
    LocalModelStatus Status { get; }
    event EventHandler? SettingsChanged;
    AutocompleteResult? GetImmediateCompletion(AutocompleteRequest request) => null;
    Task ConfigureAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default);
    Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default);
    Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default);

    /// <summary>Complete model check with steps and metrics; the default adapts <see cref="TestModelAsync"/>.</summary>
    async Task<LocalModelTestReport> RunModelTestAsync(CancellationToken cancellationToken = default)
    {
        var status = await TestModelAsync(cancellationToken).ConfigureAwait(false);
        return new(status.State == LocalModelState.Ready, status.Message, []);
    }
}
