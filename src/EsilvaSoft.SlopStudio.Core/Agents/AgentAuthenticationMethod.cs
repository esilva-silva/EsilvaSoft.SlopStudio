namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Officially supported authentication methods of an agent provider. Subscription logins (ChatGPT, Claude.ai, Codex)
/// are deliberately absent: a UI cannot offer a method that has no member here. New members require an ADR.
/// </summary>
public enum AgentAuthenticationMethod
{
    /// <summary>Local provider; no external account.</summary>
    None,

    /// <summary>User-provided API key stored in the OS vault by an explicit action.</summary>
    ApiKey,
}
