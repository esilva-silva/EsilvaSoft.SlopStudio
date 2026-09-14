using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Published model variants that can be installed as subfolders of the models directory.</summary>
public interface IRemoteModelSource
{
    Uri RepositoryUrl { get; }

    Task<IReadOnlyList<RemoteModelVariant>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and verifies every file, then installs the variant folder and returns its path. Never replaces an existing folder.</summary>
    Task<string> DownloadAsync(RemoteModelVariant variant, string modelsDirectory, IProgress<RemoteModelProgress>? progress, CancellationToken cancellationToken);
}
