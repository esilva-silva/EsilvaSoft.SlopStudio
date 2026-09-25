using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>Display wrapper of a provider descriptor. All labels derive from capabilities, never from a brand.</summary>
public sealed class AgentProviderOption(AgentProviderPresentation presentation)
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public AgentProviderPresentation Presentation { get; } = presentation;

    public string ProviderId => Presentation.ProviderId;

    public bool IsExternal => Presentation.Destination == AgentDataDestinationKind.External;

    public string DestinationText => Text.Resolve(IsExternal ? "agentDestinationExternal" : "agentDestinationLocal");

    public string DestinationHint => Text.Resolve(IsExternal ? "agentDestinationExternalHint" : "agentDestinationLocalHint");

    public string AvailabilityText => Text.Resolve(Presentation.IsAvailable ? "agentSettingsAvailable" : "agentSettingsUnavailable");

    /// <summary>"Name · Externo" or "Name · Indisponível"; destination and availability are text, not color.</summary>
    public string Label => Text.Format("agentProviderItem", Presentation.DisplayName,
        Presentation.IsAvailable ? DestinationText : AvailabilityText);

    public bool RequiresApiKey => Presentation.AuthenticationMethods.Contains(AgentAuthenticationMethod.ApiKey);

    public string AuthStateText => Text.Resolve(Presentation.AuthState switch
    {
        AgentProviderAuthState.NotRequired => "agentAuthStateNotRequired",
        AgentProviderAuthState.NotConfigured => "agentAuthStateNotConfigured",
        AgentProviderAuthState.Configured => "agentAuthStateConfigured",
        AgentProviderAuthState.Invalid => "agentAuthStateInvalid",
        AgentProviderAuthState.Expired => "agentAuthStateExpired",
        AgentProviderAuthState.Unknown => "agentAuthStateUnknown",
        _ => "agentAuthStateVaultUnavailable",
    });

    public string AuthMethodsText => Presentation.AuthenticationMethods.Count == 0
        ? Text.Resolve("agentSettingsNoAuthMethods")
        : string.Join(" · ", Presentation.AuthenticationMethods.Distinct().Select(method =>
            Text.Resolve(method == AgentAuthenticationMethod.ApiKey ? "agentAuthApiKey" : "agentAuthNone")));

    public string CapabilitiesText => Text.Format("agentCapabilitiesValue",
        Text.Resolve(Presentation.SupportsStreaming ? "agentYes" : "agentNo"),
        Text.Resolve(Presentation.SupportsToolCalling ? "agentYes" : "agentNo"));

    public string ModelsText => Presentation.Models.Count == 0
        ? Text.Resolve("agentSettingsNoModels")
        : string.Join(", ", Presentation.Models);

    public override string ToString() => Label;
}
