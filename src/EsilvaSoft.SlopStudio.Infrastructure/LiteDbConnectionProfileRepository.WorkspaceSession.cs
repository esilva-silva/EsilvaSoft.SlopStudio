using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    public Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default) => RunAsync(ReadSession, cancellationToken);

    private WorkspaceSession ReadSession()
    {
        var document = _database.GetCollection("workspaceSession").FindById("current");
        if (document is null) return new WorkspaceSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<WorkspaceSession>(document["json"].AsString)
            ?? throw new InvalidDataException("Sessão local inválida.");
        if (session.Version != 1) throw new InvalidDataException("Versão da sessão local não suportada.");
        if (session.Preferences?.Autocomplete is not { } autocomplete) throw new InvalidDataException("Preferências locais inválidas.");
        autocomplete.Validate();
        session = session with
        {
            Preferences = session.Preferences with { Language = ApplicationLanguages.Normalize(session.Preferences.Language) }
        };
        session.Preferences.ValidateUuid();
        session.Preferences.ValidateMetadata();
        session.Preferences.ValidateKeyBindings();
        return session;
    }

    public Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Version != 1) throw new ArgumentException("Versão da sessão local não suportada.", nameof(session));
        session.Preferences.Autocomplete.Validate();
        session.Preferences.ValidateUuid();
        session.Preferences.ValidateMetadata();
        session.Preferences.ValidateKeyBindings();
        // Persist policy at the boundary as well as in the UI. No credentials or results in this DTO.
        var preferences = session.Preferences with { Language = ApplicationLanguages.Normalize(session.Preferences.Language) };
        var normalized = session with { Preferences = preferences };
        var allowed = normalized.Preferences.RecoverDrafts
            ? normalized.Tabs.Where(tab => !tab.ContainsResultData && (tab.ProfileId is null || !normalized.Preferences.ExcludedProfileIds.Contains(tab.ProfileId.Value))).ToArray()
            : [];
        var filtered = normalized with { Tabs = allowed, ActiveTabId = allowed.Any(tab => tab.Id == normalized.ActiveTabId) ? normalized.ActiveTabId : null };
        return RunAsync(() =>
        {
            _ = ReadSession(); // Preserve corrupt or newer snapshots, including autocomplete configuration.
            _database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument
            {
                ["_id"] = "current",
                ["json"] = System.Text.Json.JsonSerializer.Serialize(filtered)
            });
        }, cancellationToken);
    }
}
