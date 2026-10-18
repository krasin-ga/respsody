using System;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client.Options;
using Respsody.Cluster;
using Respsody.Cluster.Options;
using Respsody.Exceptions;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class ClusterErrorObserverTests
{
    [Fact]
    public async Task MovedUpdatesNextCommandRouteBeforeTopologyRefreshCompletes()
    {
        await using var source = new ClusterTestServer();
        await using var destination = new ClusterTestServer();
        var initialTopology = source.Topology + $"node1 {destination.Address}@17001 master - 0 0 1 connected\n";
        var updatedTopology = $"node0 {source.Address}@17000 myself,master - 0 0 1 connected 0-12181 12183-16383\n"
            + $"node1 {destination.Address}@17001 master - 0 0 1 connected 12182\n";
        var refreshRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var topologyRequests = 0;
        var sourceCommands = 0;
        var destinationCommands = 0;

        async Task<string> Respond(string[] arguments, bool isSource)
        {
            if (arguments[0] == "CLUSTER")
            {
                if (Interlocked.Increment(ref topologyRequests) == 1)
                    return ClusterTestServer.Bulk(initialTopology);
                refreshRequested.TrySetResult();
                await releaseRefresh.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return ClusterTestServer.Bulk(updatedTopology);
            }

            if (isSource)
            {
                Interlocked.Increment(ref sourceCommands);
                return $"-MOVED 12182 {destination.Address}\r\n";
            }

            Interlocked.Increment(ref destinationCommands);
            return ClusterTestServer.Bulk("value");
        }

        source.Respond = (arguments, _) => Respond(arguments, true);
        destination.Respond = (arguments, _) => Respond(arguments, false);
        using var router = new ClusterRouter(new ClusterRouterOptions
        {
            SeedEndpoints = [source.Address],
            EnableAutoRedirections = false,
            ClientOptions = new RespClientOptions
            {
                RespErrorObservers = [_ => throw new InvalidOperationException("user observer failed")]
            },
            SyncInterval = TimeSpan.FromHours(1),
            CreateConnectionProcedure = (_, endpoint) =>
                (endpoint == source.Address ? source : destination).CreateConnectionProcedure()
        });

        try
        {
            await router.Initialize().WaitAsync(TimeSpan.FromSeconds(10));
            var roleRouter = router.RouteTo(RolePreference.Primary);
            using var first = roleRouter.CreateCommand<RespString>("GET").Key(Key.Utf8("foo"));
            var error = await Assert.ThrowsAsync<RespErrorResponseException>(() =>
                roleRouter.ExecuteCommand(first).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.StartsWith("MOVED 12182 ", error.Error);
            await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var next = roleRouter.CreateCommand<RespString>("GET").Key(Key.Utf8("foo"));
            using var response = await roleRouter.ExecuteCommand(next).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("value", response.ToString());
            Assert.Equal(1, Volatile.Read(ref sourceCommands));
            Assert.Equal(1, Volatile.Read(ref destinationCommands));
        }
        finally
        {
            releaseRefresh.TrySetResult();
        }
    }
}
