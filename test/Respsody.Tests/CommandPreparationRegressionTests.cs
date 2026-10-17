using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Options;
using Respsody.Exceptions;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;
using Respsody.Tests.Library;
using Xunit;

namespace Respsody.Tests;

public class CommandPreparationRegressionTests
{
    [Fact]
    public async Task FailedExecutionPreparationReturnsDeliveryAndCompletionSource()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var blocks = new MemoryBlocks();
        using var client = new RespClient(new RespClientOptions { MemoryBlocks = blocks, Handler = new ThrowingContextHandler() },
            new LoopbackConnection(listener.LocalEndPoint!));
        var start = client.Start();
        using var server = await listener.AcceptAsync(deadline.Token);
        await start.WaitAsync(deadline.Token);

        var command = client.CreateCommand<RespVoid>("PING");
        var outgoing = command.OutgoingBuffer;
        var source = SeedCompletionPool();
        var future = client.ExecuteCommand(command);
        Assert.True(future.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => future.GetAwaiter().GetResult());
        AssertSourceReturned(source);
        command.Dispose();
        AssertBufferReturned(blocks, outgoing);
    }

    [Fact]
    public void FailedPackPreparationReturnsOnlyDeliveryAndUnusedSource()
    {
        var blocks = new MemoryBlocks();
        using var client = new RespClient(new RespClientOptions { MemoryBlocks = blocks, Handler = new ThrowingContextHandler() },
            new LoopbackConnection(new IPEndPoint(IPAddress.Loopback, 0)));
        var command = client.CreateCommand<RespVoid>("PING");
        var outgoing = command.OutgoingBuffer;
        var source = SeedCompletionPool();
        Assert.Throws<InvalidOperationException>(() => client.Pack(new Combo(client), command));
        AssertSourceReturned(source);
        command.Dispose();
        AssertBufferReturned(blocks, outgoing);
    }

    [Fact]
    public void PackRejectsClientDisposedBeforeStartup()
    {
        var blocks = new MemoryBlocks();
        var client = new RespClient(new RespClientOptions { MemoryBlocks = blocks },
            new LoopbackConnection(new IPEndPoint(IPAddress.Loopback, 0)));
        var command = client.CreateCommand<RespVoid>("PING");
        var outgoing = command.OutgoingBuffer;
        client.Dispose();
        Assert.Throws<RespClientDisposedException>(() => client.Pack(new Combo(client), command));
        command.Dispose();
        AssertBufferReturned(blocks, outgoing);
    }

    private static PooledValueTaskCompletionSource<RespVoid> SeedCompletionPool()
    {
        var source = PooledValueTaskCompletionSource<RespVoid>.Create(false);
        source.SetResult(new RespVoid());
        source.AsValueTask().GetAwaiter().GetResult();
        return source;
    }

    private static void AssertSourceReturned(PooledValueTaskCompletionSource<RespVoid> expected)
    {
        var next = PooledValueTaskCompletionSource<RespVoid>.Create(false);
        try
        {
            Assert.Same(expected, next);
            next.SetResult(new RespVoid());
            Assert.True(next.AsValueTask().IsCompletedSuccessfully);
            next.AsValueTask().GetAwaiter().GetResult();
        }
        catch
        {
            next.ReturnUnused();
            throw;
        }
    }

    private static void AssertBufferReturned(MemoryBlocks blocks, OutgoingBuffer expected)
    {
        var next = blocks.LeaseLinked(128, true, 16);
        try
        {
            Assert.Same(expected, next);
        }
        finally
        {
            next.Commit();
            next.FreeByDeliveryPipeline();
            next.FreeByOwner();
        }
    }

    private sealed class ThrowingContextHandler : TestHandler, IRespClientHandler
    {
        object? IRespClientHandler.CreateCommandContext(IRespClient client, string command)
            => throw new InvalidOperationException("Context creation failed.");
    }

    private sealed class LoopbackConnection(EndPoint endpoint) : IConnectionProcedure
    {
        public IReadOnlyDictionary<string, string?> Config => new Dictionary<string, string?>();
        public TimeSpan Timeout => TimeSpan.FromSeconds(10);

        public async Task<ConnectedSocket> Connect(CancellationToken token)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(endpoint, token);
            return new ConnectedSocket(socket, new ConnectionMetadata(endpoint, new Dictionary<string, object?>()));
        }
    }
}
