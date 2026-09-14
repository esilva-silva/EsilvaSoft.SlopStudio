using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IExplorerMetadataService
{
    Task<TopologyInfo> GetTopologyAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);
    Task<string> GetCollectionDetailsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);
}
