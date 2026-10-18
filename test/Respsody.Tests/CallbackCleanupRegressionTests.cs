using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Options;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;
using Respsody.Tests.Library;
using Xunit;

namespace Respsody.Tests;

public class CallbackCleanupRegressionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ThrowingCancellationStillDrainsEveryBufferAndDisposesFraming(bool start, bool batch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var blocks = new MemoryBlocks();
        var framing = new TrackingFraming(blocks);
        var connection = new NeverConnect();
        var socket = new StructuredSocket<RespContext, ThrowingPayload>(connection, framing,
            new StructuredSocketOptions(), new SocketHandler());
        var payloads = new[] { new ThrowingPayload(), new ThrowingPayload(), new ThrowingPayload() };
        var pages = new OutgoingBuffer[payloads.Length];
        var units = new TransmitUnit<ThrowingPayload>[payloads.Length];
        for (var i = 0; i < payloads.Length; i++)
        {
            pages[i] = blocks.LeaseLinked(128, false, 0);
            pages[i].Write("PING"u8);
            pages[i].Commit();
            pages[i].FreeByOwner();
            units[i] = new TransmitUnit<ThrowingPayload>(pages[i], payloads[i]);
        }
        if (batch)
            socket.Send(units);
        else
            foreach (var unit in units)
                socket.Send(unit);

        if (start)
        {
            socket.Start();
            await connection.Entered.Task.WaitAsync(deadline.Token);
            socket.Dispose();
            await Assert.ThrowsAsync<InvalidOperationException>(() => socket.Completion.WaitAsync(deadline.Token));
        }
        else
            Assert.Throws<InvalidOperationException>(() => socket.Dispose());

        socket.Dispose();
        Assert.Equal(1, framing.Disposals);
        Assert.All(payloads, payload => Assert.Equal(1, payload.Cancellations));
        AssertBuffersReturned(blocks, pages);
    }

    [Fact]
    public void DisconnectCompletesEveryResponseDespiteDiagnosticFailures()
    {
        var handler = new ThrowingHandler();
        using var client = new RespClient(new RespClientOptions { Handler = handler }, new NeverConnect());
        var futures = new ValueTask<RespVoid>[3];
        var failure = new Exception("Connection lost.");
        for (var i = 0; i < futures.Length; i++)
        {
            var source = PooledValueTaskCompletionSource<RespVoid>.Create(false);
            futures[i] = source.AsValueTask();
            var payload = new RespClient.Payload(client, source, ResponseType.Void, "PING", Environment.TickCount);
            Assert.True(payload.OnAboutToWrite(1, Environment.TickCount));
        }

        Assert.Throws<InvalidOperationException>(() => client.OnDisconnected(failure, 1));
        Assert.Equal(1, handler.Disconnections);
        Assert.Equal(futures.Length, handler.Failures);
        foreach (var future in futures)
        {
            Assert.True(future.IsCompleted);
            Assert.Same(failure, Assert.Throws<Exception>(() => future.GetAwaiter().GetResult()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpirationFailureReleasesEveryMemberOfDequeuedBatch(bool throwOnExpire)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var blocks = new MemoryBlocks();
        var framing = new TrackingFraming(blocks);
        var socket = new StructuredSocket<RespContext, ThrowingPayload>(new NeverConnect(), framing,
            new StructuredSocketOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(1) }, new SocketHandler());
        var payloads = new[] { new ThrowingPayload { Expire = true, ThrowOnExpire = throwOnExpire }, new ThrowingPayload(), new ThrowingPayload() };
        var pages = new OutgoingBuffer[payloads.Length];
        var units = new TransmitUnit<ThrowingPayload>[payloads.Length];
        for (var i = 0; i < payloads.Length; i++)
        {
            pages[i] = blocks.LeaseLinked(128, false, 0);
            pages[i].Write("PING"u8);
            pages[i].Commit();
            pages[i].FreeByOwner();
            units[i] = new TransmitUnit<ThrowingPayload>(pages[i], payloads[i]);
        }
        socket.Send(units);
        socket.Start();
        await payloads[0].Checked.Task.WaitAsync(deadline.Token);
        socket.Dispose();
        await socket.Completion.WaitAsync(deadline.Token);
        Assert.Equal(throwOnExpire ? 1 : 0, payloads[0].Cancellations);
        Assert.Equal(1, payloads[1].Cancellations);
        Assert.Equal(1, payloads[2].Cancellations);
        AssertBuffersReturned(blocks, pages);
    }

    [Fact]
    public void ComboRejectionReleasesEveryCommandDespiteDiagnosticFailures()
    {
        var blocks = new MemoryBlocks();
        var handler = new ThrowingHandler();
        using var client = new RespClient(new RespClientOptions { Handler = handler, MemoryBlocks = blocks }, new NeverConnect());
        var combo = new Combo(client);
        var futures = new ValueTask<RespVoid>[3];
        var pages = new OutgoingBuffer[3];
        for (var i = 0; i < futures.Length; i++)
        {
            var command = combo.CreateCommand(static c => c.CreateCommand<RespVoid>("PING"));
            futures[i] = command.Future();
            pages[i] = command.TransmitUnit.Page;
        }

        Assert.Throws<InvalidOperationException>(() => combo.Dispose());
        combo.Dispose();
        Assert.Equal(futures.Length, handler.Failures);
        foreach (var future in futures)
        {
            Assert.True(future.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => future.GetAwaiter().GetResult());
        }
        AssertBuffersReturned(blocks, pages);
    }

    private static void AssertBuffersReturned(MemoryBlocks blocks, OutgoingBuffer[] expected)
    {
        var observed = new List<OutgoingBuffer>();
        foreach (var page in expected)
        {
            var next = blocks.LeaseLinked(128, false, 0);
            Assert.Contains(next, expected);
            Assert.DoesNotContain(next, observed);
            observed.Add(next);
            next.Commit();
        }
        foreach (var next in observed)
        {
            next.FreeByOwner();
            next.FreeByDeliveryPipeline();
        }
    }

    private sealed class ThrowingPayload : IPayload
    {
        public int Cancellations { get; private set; }
        public bool Expire { get; init; }
        public bool ThrowOnExpire { get; init; }
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool OnAboutToWrite(int socketId, int ticks) => true;
        public bool TryExpire(int ticks)
        {
            Checked.TrySetResult();
            if (ThrowOnExpire)
                throw new InvalidOperationException("Expiration callback failed.");
            return Expire;
        }
        public void LinkedCancel(int ticks)
        {
            Cancellations++;
            throw new InvalidOperationException("Cancellation callback failed.");
        }
    }

    private sealed class ThrowingHandler : TestHandler, IRespClientHandler
    {
        public int Disconnections { get; private set; }
        public int Failures { get; private set; }

        void IRespClientHandler.OnDisconnected(IRespClient client, Exception? exception, int generation)
        {
            Disconnections++;
            throw new InvalidOperationException("Disconnect callback failed.");
        }

        void IRespClientHandler.OnCommandFailed(IRespClient client, int elapsedTimeInMs, string command, object? context)
        {
            Failures++;
            throw new InvalidOperationException("Failure callback failed.");
        }
    }

    private sealed class TrackingFraming(MemoryBlocks blocks) : Framing<RespContext>(new WindowSize(1), blocks, 64)
    {
        public int Disposals { get; private set; }
        protected override Sequence<RespContext> Create() => new();
        protected override Decision Advance(ref Sequence<RespContext> sequence, ReadOnlySpan<byte> window) => default;
        public override void Dispose()
        {
            Disposals++;
            base.Dispose();
        }
    }

    private sealed class NeverConnect : IConnectionProcedure
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyDictionary<string, string?> Config => new Dictionary<string, string?>();
        public TimeSpan Timeout => TimeSpan.FromSeconds(10);
        public async Task<ConnectedSocket> Connect(CancellationToken token)
        {
            Entered.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }
    }

    private sealed class SocketHandler : IStructuredSocketHandler<RespContext>
    {
        public ValueTask InitializeConnection(ConnectedSocket socket, int generation, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask OnConnected(int generation, CancellationToken token) => ValueTask.CompletedTask;
        public void OnDisconnected(Exception? exception, int generation) { }
        public void HandleConnectionError(Exception exception) { }
        public void HandleIncoming(ReadyFrames<RespContext> frames) => throw new InvalidOperationException();
    }
}
