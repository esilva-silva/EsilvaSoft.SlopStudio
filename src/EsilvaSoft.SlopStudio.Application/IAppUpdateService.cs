using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Disabled outside a published executable; ManualOnly when the installation folder is not writable.</summary>
public enum AppUpdateAvailability { Disabled, ManualOnly, Supported }

/// <summary>A verified package waiting for the application to exit; <paramref name="LastApplyError"/> reports a failed previous attempt.</summary>
public sealed record StagedAppUpdate(AppVersion Version, string? LastApplyError);

public interface IAppUpdateService
{
    AppUpdateAvailability Availability { get; }
    AppVersion CurrentVersion { get; }

    /// <summary>Update downloaded in this or a previous session and not applied yet.</summary>
    StagedAppUpdate? GetStagedUpdate();

    /// <summary>Returns null when there is no newer release or the feed is unreachable.</summary>
    Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads, verifies and stages the package; it is installed after the application exits.</summary>
    Task<StagedAppUpdate> DownloadAsync(AppUpdateRelease release, ApplicationOperationScope operation);
}
