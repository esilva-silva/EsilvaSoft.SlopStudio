using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.LocalAi.Core;

public interface ILocalModelCatalog
{
    string DefaultDirectory { get; }
    Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default);
    /// <summary>Each immediate subfolder of <paramref name="directory"/> is a candidate.</summary>
    Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(string directory, CancellationToken cancellationToken = default) => DiscoverAsync(cancellationToken);
    Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default);
}
