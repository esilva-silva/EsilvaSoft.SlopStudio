using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed record ConnectionChoice(ConnectionProfile Profile)
{
    public string Name => (Profile.IsFavorite ? "★ " : "") + Profile.Name;
    public string Details => $"{Profile.Folder ?? "Sem pasta"} · {Profile.Environment ?? "Sem ambiente"}";
    public string Endpoint => Profile.Endpoint;
}
