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
        var allowed = session.Preferences.RecoverDrafts
            ? session.Tabs.Where(tab => !tab.ContainsResultData && (tab.ProfileId is null || !session.Preferences.ExcludedProfileIds.Contains(tab.ProfileId.Value))).ToArray()
            : [];
        var filtered = session with { Tabs = allowed, ActiveTabId = allowed.Any(tab => tab.Id == session.ActiveTabId) ? session.ActiveTabId : null };
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
