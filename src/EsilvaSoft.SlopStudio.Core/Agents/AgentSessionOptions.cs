namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Fixed session request. <paramref name="WorkingDirectory"/> is the local workspace folder the caller captured before
/// starting the session (on the UI thread, from the Files panel); providers that run a local official CLI use it as a
/// candidate working directory and validate it, the others ignore it. It is never read back from UI state later.
/// </summary>
public sealed record AgentSessionOptions(string ProviderId, string? ModelId = null, string? WorkingDirectory = null);
