using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class CommandOwnershipTests
{
    [Fact]
    public async Task LazyConnectionHasOnlyOneDeliveryLease()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var blocks = new MemoryBlocks();
        using var client = new RespClient(new RespClientOptions { MemoryBlocks = blocks },
            new LoopbackConnection(listener.LocalEndPoint!));
        var command = client.CreateCommand<RespString>("PING");
        var outgoing = command.OutgoingBuffer;
        var response = client.ExecuteCommand(command, deadline.Token);
        using var server = await listener.AcceptAsync(deadline.Token);
        var request = new byte[14];
        for (var offset = 0; offset < request.Length;)
            offset += await server.ReceiveAsync(request.AsMemory(offset), SocketFlags.None, deadline.Token);
        Assert.Equal("*1\r\n$4\r\nPING\r\n"u8.ToArray(), request);
        await server.SendAsync("+PONG\r\n"u8.ToArray(), SocketFlags.None, deadline.Token);
        using (var result = await response)
            Assert.Equal("PONG", result.ToString());
        command.Dispose();

        AssertReturned(blocks, outgoing);
    }

    [Fact]
    public async Task FailedLazyConnectionReleasesReservedDelivery()
    {
        var blocks = new MemoryBlocks();
        using var client = new RespClient(new RespClientOptions { MemoryBlocks = blocks }, new NeverConnect());
        var command = client.CreateCommand<RespVoid>("PING");
        var outgoing = command.OutgoingBuffer;
        await Assert.ThrowsAsync<TimeoutException>(async () => await client.ExecuteCommand(command));
        command.Dispose();

        AssertReturned(blocks, outgoing);
    }

    [Fact]
    public void DisposingUnsentCommandReturnsItsOutgoingBuffer()
    {
        var blocks = new MemoryBlocks();
        using var client = new RespClient(new RespClientOptions { MemoryBlocks = blocks },
            new DefaultConnectionProcedure(new ConnectionOptions()));
        var command = client.CreateCommand<RespVoid>("PING");
        var outgoing = command.OutgoingBuffer;

        command.Dispose();

        AssertReturned(blocks, outgoing);
    }

    private static void AssertReturned(MemoryBlocks blocks, OutgoingBuffer outgoing)
    {
        var reused = blocks.LeaseLinked(128, true, 16);
        try
        {
            Assert.Same(outgoing, reused);
        }
        finally
        {
            reused.Commit();
            reused.FreeByDeliveryPipeline();
            reused.FreeByOwner();
        }
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

    private sealed class NeverConnect : IConnectionProcedure
    {
        public IReadOnlyDictionary<string, string?> Config => new Dictionary<string, string?>();
        public TimeSpan Timeout => TimeSpan.FromMilliseconds(100);
        public async Task<ConnectedSocket> Connect(CancellationToken token)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            throw new InvalidOperationException();
        }
    }
}
