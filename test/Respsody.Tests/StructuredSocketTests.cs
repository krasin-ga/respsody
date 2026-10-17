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
        using var framing = new RespFraming(blocks, 4096);
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

    private sealed class Connection(Socket socket) : IConnectionProcedure
    {
        public IReadOnlyDictionary<string, string?> Config => new Dictionary<string, string?>();
        public TimeSpan Timeout => TimeSpan.FromSeconds(10);
        public Task<ConnectedSocket> Connect(CancellationToken token)
            => Task.FromResult(new ConnectedSocket(socket,
                new ConnectionMetadata(socket.RemoteEndPoint!, new Dictionary<string, object?>())));
    }

    private sealed class Payload : IPayload
    {
        public TaskCompletionSource Writing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool OnAboutToWrite(int socketId, int ticks)
        {
            Writing.TrySetResult();
            return true;
        }
        public bool TryExpire(int ticks) => false;
        public void LinkedCancel(int ticks) { }
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
