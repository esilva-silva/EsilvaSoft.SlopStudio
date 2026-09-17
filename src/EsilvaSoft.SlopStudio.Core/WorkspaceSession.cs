namespace EsilvaSoft.SlopStudio.Core;

public sealed record WorkspaceSession
{
    public int Version { get; init; } = 1;
    public WorkspacePreferences Preferences { get; init; } = new();
    public Guid? ActiveTabId { get; init; }
    public WorkspaceDraft[] Tabs { get; init; } = [];
}
