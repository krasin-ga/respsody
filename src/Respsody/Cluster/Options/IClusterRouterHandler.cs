using Respsody.Client;

namespace Respsody.Cluster.Options;

public interface IClusterRouterHandler
{
    void OnFailedToSyncClusterState(ClusterRouter router, IReadOnlyList<Exception> exception);
    void OnClusterStateSynced(ClusterRouter router, ClusterState state);
    void OnFailedToRefreshClusterSateFromSeed(ClusterRouter router, string seedEndpoint, Exception exception);
    void OnClientDisposed(ClusterRouter router, IRespClient respClient);
}