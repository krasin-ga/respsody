using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;
using Respsody.Resp.Parsing;
using Xunit;

namespace Respsody.Tests;

public class StructuredSocketTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalCancelsQueuedCommandsAndIsIdempotent(bool start)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var unusedSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocks = new MemoryBlocks();
        var framing = new RespFraming(blocks, 4096);
        var socket = new StructuredSocket<RespContext, Payload>(new Connection(unusedSocket, gate.Task), framing,
            new StructuredSocketOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(10) }, new Handler());
        var payload = new Payload();
        var page = Page(blocks, (byte)'A');
        socket.Send(new TransmitUnit<Payload>(page, payload));
        if (start)
        {
            socket.Start();
            await payload.Checked.Task.WaitAsync(deadline.Token);
        }

        socket.Dispose();
        socket.Dispose();
        await socket.Completion.WaitAsync(deadline.Token);

        Assert.Equal(1, payload.Cancellations);
        var reused = blocks.LeaseLinked(4096, false, 0);
        Assert.Same(page, reused);
        reused.Commit();
        reused.FreeByOwner();
        reused.FreeByDeliveryPipeline();
    }

    [Fact]
    public async Task LiveBatchSurvivesDisconnectedExpiration()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var sender = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await sender.ConnectAsync(listener.LocalEndPoint!, deadline.Token);
        using var receiver = await listener.AcceptAsync(deadline.Token);
        var connect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocks = new MemoryBlocks();
        var framing = new RespFraming(blocks, 4096);
        var handler = new Handler();
        using var socket = new StructuredSocket<RespContext, Payload>(new Connection(sender, connect.Task), framing,
            new StructuredSocketOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(10) }, handler);
        var first = new Payload();
        socket.Send(new[]
        {
            new TransmitUnit<Payload>(Page(blocks, (byte)'A'), first),
            new TransmitUnit<Payload>(Page(blocks, (byte)'B'), new Payload())
        });
        socket.Start();
        await first.Checked.Task.WaitAsync(deadline.Token);
        var last = new Payload();
        socket.Send(new TransmitUnit<Payload>(Page(blocks, (byte)'C'), last));
        await last.Checked.Task.WaitAsync(deadline.Token);
        connect.SetResult();

        var result = new byte[3];
        for (var offset = 0; offset < result.Length;)
            offset += await receiver.ReceiveAsync(result.AsMemory(offset), SocketFlags.None, deadline.Token);
        Assert.Equal("ABC"u8.ToArray(), result);
    }

    private static OutgoingBuffer Page(MemoryBlocks blocks, byte value)
    {
        var page = blocks.LeaseLinked(4096, false, 0);
        page.Write(new[] { value });
        page.Commit();
        page.FreeByOwner();
        return page;
    }

    [Fact]
    public async Task DisposalInterruptsScatterSendToPeerThatDoesNotRead()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var sender = new Socket(SocketType.Stream, ProtocolType.Tcp) { SendBufferSize = 4096 };
        await sender.ConnectAsync(listener.LocalEndPoint!, deadline.Token);
        using var receiver = await listener.AcceptAsync(deadline.Token);
        var blocks = new MemoryBlocks();
        var framing = new RespFraming(blocks, 4096);
        var handler = new Handler();
        var socket = new StructuredSocket<RespContext, Payload>(new Connection(sender), framing,
            new StructuredSocketOptions { OutgoingBufferSize = 4096 }, handler);
        using (socket)
        {
            socket.Start();
            await handler.Connected.Task.WaitAsync(deadline.Token);
            var page = blocks.LeaseLinked(128 * 1024, false, 0);
            page.Write(new byte[16 * 1024 * 1024]);
            page.Commit();
            page.FreeByOwner();
            var payload = new Payload();
            socket.Send(new TransmitUnit<Payload>(page, payload));
            await payload.Writing.Task.WaitAsync(deadline.Token);
        }

        await handler.Disconnected.Task.WaitAsync(deadline.Token);
    }

    private sealed class Connection(Socket socket, Task? gate = null) : IConnectionProcedure
    {
        public IReadOnlyDictionary<string, string?> Config => new Dictionary<string, string?>();
        public TimeSpan Timeout => TimeSpan.FromSeconds(10);
        public async Task<ConnectedSocket> Connect(CancellationToken token)
        {
            if (gate is not null)
                await gate.WaitAsync(token);
            return new ConnectedSocket(socket,
                new ConnectionMetadata(socket.RemoteEndPoint!, new Dictionary<string, object?>()));
        }
    }

    private sealed class Payload : IPayload
    {
        public int Cancellations { get; private set; }
        public TaskCompletionSource Writing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool OnAboutToWrite(int socketId, int ticks)
        {
            Writing.TrySetResult();
            return true;
        }
        public bool TryExpire(int ticks)
        {
            Checked.TrySetResult();
            return false;
        }
        public void LinkedCancel(int ticks) => Cancellations++;
    }

    private sealed class Handler : IStructuredSocketHandler<RespContext>
    {
        public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask InitializeConnection(ConnectedSocket socket, int generation, CancellationToken token)
            => ValueTask.CompletedTask;
        public ValueTask OnConnected(int generation, CancellationToken token)
        {
            Connected.TrySetResult();
            return ValueTask.CompletedTask;
        }
        public void OnDisconnected(Exception? exception, int generation) => Disconnected.TrySetResult();
        public void HandleConnectionError(Exception exception) { }
        public void HandleIncoming(ReadyFrames<RespContext> frames)
        {
            using (frames)
                foreach (var frame in frames)
                    frame.Dispose();
        }
    }
}
