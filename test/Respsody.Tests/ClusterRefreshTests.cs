using System;
using System.Threading.Tasks;
using Respsody.Client.Options;
using Respsody.Cluster;
using Respsody.Cluster.Options;
using Respsody.Exceptions;
using Xunit;

namespace Respsody.Tests;

public class ClusterRefreshTests
{
    [Fact]
    public async Task RefreshReusesPublishedSeedClientAndDisposesProbe()
    {
        await using var server = new ClusterTestServer();
        using var router = CreateRouter(server);
        await router.Initialize().WaitAsync(TimeSpan.FromSeconds(10));
        var original = router.PickRandomPrimary();

        var (succeeded, exceptions) = await router.SyncClusterState().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(succeeded);
        Assert.Empty(exceptions);
        Assert.Same(original, router.PickRandomPrimary());
        await server.ConnectionClosed(2);
        Assert.False(server.IsClosed(1));

    }

    [Theory]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("node0 127.0.0.1:7000@17000 myself,master - 0 0 1 connected 65535")]
    public async Task FailedTopologyDisposesProbe(string topology)
    {
        await using var server = new ClusterTestServer
        {
            Respond = (_, _) => Task.FromResult(ClusterTestServer.Bulk(topology))
        };
        using var router = CreateRouter(server);

        await Assert.ThrowsAsync<RespClusterInitializationFailedException>(() =>
            router.Initialize().WaitAsync(TimeSpan.FromSeconds(10)));

        await server.ConnectionClosed(1);
    }

    [Fact]
    public async Task NonClusterSeedIsDisposed()
    {
        await using var server = new ClusterTestServer { Mode = "standalone" };
        using var router = CreateRouter(server);

        await Assert.ThrowsAsync<RespClusterInitializationFailedException>(() =>
            router.Initialize().WaitAsync(TimeSpan.FromSeconds(10)));

        await server.ConnectionClosed(1);
    }

    internal static ClusterRouter CreateRouter(ClusterTestServer server) => new(new ClusterRouterOptions
    {
        SeedEndpoints = [server.Address],
        ClientOptions = new RespClientOptions(),
        SyncInterval = TimeSpan.FromHours(1),
        CreateConnectionProcedure = (_, _) => server.CreateConnectionProcedure()
    });
}
