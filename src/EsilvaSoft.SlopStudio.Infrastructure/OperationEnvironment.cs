using System.Collections;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

internal sealed class OperationEnvironment
{
    public EnvironmentSnapshot Vault { get; }
    private readonly Dictionary<string, string> _legacy;
    public string ResolvedConnection { get; private set; } = "";

    public OperationEnvironment(IEnvironmentVaultRepository? repository, IConnectionSecretStore? secrets, Guid profileId)
    {
        Vault = (repository?.LoadEnvironments() ?? EnvironmentVault.CreateDefault()).Capture();
        _legacy = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (secrets?.GetPassword(profileId) is { } password) _legacy["MONGODB_PASSWORD"] = password;
    }

    public string Get(string key) => Vault.Values.TryGetValue(key, out var value) ? value
        : _legacy.TryGetValue(key, out value) ? value : Vault.Get(key);
    public string? GetLegacy(string key) => _legacy.GetValueOrDefault(key);
    public string ResolveConnection(ConnectionProfile profile) => ConnectionRouting.Apply(profile.ResolveConnectionString(
        key => _legacy.GetValueOrDefault(key), Get), profile.TargetHost);
    public async Task PrepareAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var uri = profile.ResolveConnectionString(key => _legacy.GetValueOrDefault(key), Get);
        ResolvedConnection = await ConnectionRouting.ApplyAsync(uri, profile.TargetHost, cancellationToken).ConfigureAwait(false);
    }
    public IReadOnlyDictionary<string, string> ScriptValues => _legacy.Concat(Vault.Values)
        .GroupBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value);
}
