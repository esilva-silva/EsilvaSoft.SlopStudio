using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;

public interface ICompletionProfileResolver
{
    /// <summary>Resolves a transient driver profile from identity; the result must not be retained in request snapshots.</summary>
    ConnectionProfile? Resolve(Guid profileId);
}
