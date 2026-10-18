using System;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Options;
using Respsody.Cluster;
using Respsody.Cluster.Options;
using Respsody.Exceptions;
using Respsody.Library;
using Respsody.Resp;
using Respsody.Tests.Library;
using Xunit;

namespace Respsody.Tests;

public class ClusterAskingSetupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAskingPreparationConsumesAllCreatedFutures(bool disposeBeforeSubmission)
    {
        await using var source = new ClusterTestServer();
        await using var destination = new ClusterTestServer();
        var topology = source.Topology + $"node1 {destination.Address}@17001 master - 0 0 1 connected\n";
        var redirectedCommands = 0;
        source.Respond = (arguments, _) => Task.FromResult(arguments[0] == "CLUSTER"
            ? ClusterTestServer.Bulk(topology)
            : $"-ASK 12182 {destination.Address}\r\n");
        destination.Respond = (arguments, _) =>
        {
            if (arguments[0] == "CLUSTER")
                return Task.FromResult(ClusterTestServer.Bulk(topology));
            if (arguments[0] is "ASKING" or "GET")
                Interlocked.Increment(ref redirectedCommands);
            return Task.FromResult("+OK\r\n");
        };
        var handler = new FailingSetupHandler(disposeBeforeSubmission);
        using var router = new ClusterRouter(new ClusterRouterOptions
        {
            SeedEndpoints = [source.Address],
            EnableAutoRedirections = true,
            ClientOptions = new RespClientOptions { Handler = handler, RunContinuationsAsynchronously = false },
            SyncInterval = TimeSpan.FromHours(1),
            CreateConnectionProcedure = (_, endpoint) =>
                (endpoint == source.Address ? source : destination).CreateConnectionProcedure()
        });
        await router.Initialize().WaitAsync(TimeSpan.FromSeconds(10));
        var roleRouter = router.RouteTo(RolePreference.Primary);
        using var command = roleRouter.CreateCommand<RespVoid>("GET").Key(Key.Utf8("foo"));
        var askingSource = SeedCompletionPool();
        var result = roleRouter.ExecuteCommand(command).AsTask();

        if (disposeBeforeSubmission)
            await Assert.ThrowsAsync<RespClientDisposedException>(() => result.WaitAsync(TimeSpan.FromSeconds(10)));
        else
            Assert.Same(handler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
                result.WaitAsync(TimeSpan.FromSeconds(10))));

        Assert.Equal(2, handler.PackedCommands);
        Assert.False(askingSource.AsValueTask().IsCompleted);
        Assert.NotNull(handler.CommandSource);
        Assert.False(handler.CommandSource.AsValueTask().IsCompleted);
        Assert.Equal(0, Volatile.Read(ref redirectedCommands));
    }

    private static PooledValueTaskCompletionSource<RespVoid> SeedCompletionPool()
    {
        var source = PooledValueTaskCompletionSource<RespVoid>.Create(false);
        source.SetResult(new RespVoid());
        source.AsValueTask().GetAwaiter().GetResult();
        return source;
    }

    private sealed class FailingSetupHandler(bool disposeBeforeSubmission) : TestHandler, IRespClientHandler
    {
        public InvalidOperationException Failure { get; } = new("ASK preparation failed.");
        public PooledValueTaskCompletionSource<RespVoid>? CommandSource { get; private set; }
        public int PackedCommands { get; private set; }

        object? IRespClientHandler.CreateCommandContext(IRespClient client, string command)
        {
            if (command != "<combo>")
                return null;
            if (++PackedCommands == 1)
            {
                CommandSource = SeedCompletionPool();
                return null;
            }
            if (!disposeBeforeSubmission)
                throw Failure;
            ((RespClient)client).Dispose();
            return null;
        }
    }
}
