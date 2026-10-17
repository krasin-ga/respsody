using System;
using System.Threading.Tasks;
using Respsody.Exceptions;
using Xunit;

namespace Respsody.Tests;

public class ClusterLifecycleTests
{
    [Fact]
    public async Task PrematureRefreshDoesNotPreventInitialization()
    {
        await using var server = new ClusterTestServer();
        using var router = ClusterRefreshTests.CreateRouter(server);

        await Assert.ThrowsAsync<RespClusterNotInitializedException>(() => router.SyncClusterState());
        await router.Initialize().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(router.PickRandomPrimary());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalStopsInFlightTopologyPublication(bool initializeFirst)
    {
        await using var server = new ClusterTestServer();
        using var router = ClusterRefreshTests.CreateRouter(server);
        var roleRouter = router.RouteTo(RolePreference.Primary);
        if (initializeFirst)
            await router.Initialize().WaitAsync(TimeSpan.FromSeconds(10));

        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Respond = async (_, _) =>
        {
            requestReceived.TrySetResult();
            await releaseResponse.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return ClusterTestServer.Bulk(server.Topology);
        };

        var refresh = initializeFirst ? (Task)router.SyncClusterState() : router.Initialize();
        try
        {
            await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            router.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => refresh.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Throws<ObjectDisposedException>(() => roleRouter.PickNode((ushort)0));
            Assert.Throws<ObjectDisposedException>(() => router.PickRandomPrimary());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => router.SyncClusterState());
        }
        finally
        {
            releaseResponse.TrySetResult();
        }

        for (var index = 1; index <= server.ConnectionCount; index++)
            await server.ConnectionClosed(index);
    }

}
