using System;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client.Options;
using Respsody.Cluster;
using Respsody.Cluster.Options;
using Respsody.Resp;
using Respsody.Exceptions;
using Xunit;

namespace Respsody.Tests;

public class ClusterRedirectionTests
{
    [Fact]
    public async Task AskingFailureIsObservedEvenWhenFollowingCommandSucceeds()
    {
        await using var source = new ClusterTestServer();
        await using var destination = new ClusterTestServer();
        var topology = source.Topology + $"node1 {destination.Address}@17001 master - 0 0 1 connected\n";
        source.Respond = (arguments, _) => Task.FromResult(arguments[0] == "CLUSTER"
            ? ClusterTestServer.Bulk(topology)
            : $"-ASK 12182 {destination.Address}\r\n");
        destination.Respond = (arguments, _) => Task.FromResult(arguments[0] switch
        {
            "CLUSTER" => ClusterTestServer.Bulk(topology),
            "ASKING" => "-ERR asking denied\r\n",
            _ => ClusterTestServer.Bulk("value")
        });
        using var router = new ClusterRouter(new ClusterRouterOptions
        {
            SeedEndpoints = [source.Address],
            EnableAutoRedirections = true,
            ClientOptions = new RespClientOptions(),
            SyncInterval = TimeSpan.FromHours(1),
            CreateConnectionProcedure = (_, endpoint) =>
                (endpoint == source.Address ? source : destination).CreateConnectionProcedure()
        });
        await router.Initialize().WaitAsync(TimeSpan.FromSeconds(10));
        var roleRouter = router.RouteTo(RolePreference.Primary);
        using var command = roleRouter.CreateCommand<RespString>("GET").Key(Key.Utf8("foo"));

        var exception = await Assert.ThrowsAsync<RespErrorResponseException>(() =>
            roleRouter.ExecuteCommand(command).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal("ERR asking denied", exception.Error);
    }

    [Theory]
    [InlineData("MOVED", 5000)]
    [InlineData("ASK", 5000)]
    [InlineData("MOVED", 500)]
    [InlineData("ASK", 500)]
    public async Task RedirectionConsumesWholeElapsedTimeout(string redirect, int timeout)
    {
        await using var source = new ClusterTestServer();
        await using var destination = new ClusterTestServer();
        var topology = source.Topology + $"node1 {destination.Address}@17001 master - 0 0 1 connected\n";
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var redirectedCommands = 0;
        source.Respond = async (arguments, _) =>
        {
            if (arguments[0] == "CLUSTER")
                return ClusterTestServer.Bulk(topology);
            requestReceived.TrySetResult();
            await releaseResponse.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return $"-{redirect} 12182 {destination.Address}\r\n";
        };
        destination.Respond = (arguments, _) =>
        {
            if (arguments[0] == "CLUSTER")
                return Task.FromResult(ClusterTestServer.Bulk(topology));
            Interlocked.Increment(ref redirectedCommands);
            return Task.FromResult(arguments[0] == "ASKING" ? "+OK\r\n" : ClusterTestServer.Bulk("value"));
        };
        using var router = new ClusterRouter(new ClusterRouterOptions
        {
            SeedEndpoints = [source.Address],
            EnableAutoRedirections = true,
            ClientOptions = new RespClientOptions { TimeoutCheckInterval = TimeSpan.Zero },
            SyncInterval = TimeSpan.FromHours(1),
            CreateConnectionProcedure = (_, endpoint) =>
                (endpoint == source.Address ? source : destination).CreateConnectionProcedure()
        });
        await router.Initialize().WaitAsync(TimeSpan.FromSeconds(10));
        var roleRouter = router.RouteTo(RolePreference.Primary);
        using var command = roleRouter.CreateCommand<RespString>("GET").Key(Key.Utf8("foo")).WithTimeout(timeout);
        var result = roleRouter.ExecuteCommand(command).AsTask();

        try
        {
            await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(1100);
        }
        finally
        {
            releaseResponse.TrySetResult();
        }

        if (timeout < 1100)
        {
            await Assert.ThrowsAsync<TimeoutException>(() => result.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(0, Volatile.Read(ref redirectedCommands));
        }
        else
        {
            using var response = await result.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("value", response.ToString());
            Assert.InRange(command.Timeout, 1, timeout - 1000);
            Assert.Equal(redirect == "ASK" ? 2 : 1, Volatile.Read(ref redirectedCommands));
        }
    }
}
