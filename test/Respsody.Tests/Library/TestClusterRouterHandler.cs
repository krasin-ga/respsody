using System;
using System.Collections.Generic;
using Respsody.Client;
using Respsody.Cluster;
using Respsody.Cluster.Options;
using Xunit.Abstractions;

namespace Respsody.Tests.Library;

public class TestClusterRouterHandler(
    ITestOutputHelper? output = null,
    Action<ClusterRouter, IReadOnlyList<Exception>>? onFailedToSyncClusterState = null,
    Action<ClusterRouter, ClusterState>? onClusterStateSynced = null,
    Action<ClusterRouter, string, Exception>? onFailedToRefreshClusterStateFromSeed = null,
    Action<ClusterRouter, IRespClient>? onClientDisposed = null)
    : IClusterRouterHandler
{
    public void OnFailedToSyncClusterState(ClusterRouter router, IReadOnlyList<Exception> exceptions)
    {
        output?.WriteLine($"[OnFailedToSyncClusterState] Router: {router}, Exceptions: {exceptions.Count}");
        foreach (var ex in exceptions)
            output?.WriteLine($"  Exception: {ex}");

        onFailedToSyncClusterState?.Invoke(router, exceptions);
    }

    public void OnClusterStateSynced(ClusterRouter router, ClusterState state)
    {
        output?.WriteLine($"[OnClusterStateSynced] State: {state}");
        onClusterStateSynced?.Invoke(router, state);
    }

    public void OnFailedToRefreshClusterSateFromSeed(ClusterRouter router, string seedEndpoint, Exception exception)
    {
        output?.WriteLine($"[OnFailedToRefreshClusterSateFromSeed] Seed: {seedEndpoint}, Exception: {exception}");
        onFailedToRefreshClusterStateFromSeed?.Invoke(router, seedEndpoint, exception);
    }

    public void OnClientDisposed(ClusterRouter router, IRespClient respClient)
    {
        output?.WriteLine($"[OnClientDisposed] Client: {respClient}");
        onClientDisposed?.Invoke(router, respClient);
    }
}