using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Options;
using Respsody.Library;
using Respsody.Network;
using Respsody.Resp;
using Respsody.Tests.Library;
using Xunit;

namespace Respsody.Tests;

public class CallbackCompletionRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExecutedCallbackFailureCompletesFutureAndReleasesIncomingOwnership(bool aggregate)
    {
        using var client = new RespClient(new RespClientOptions { Handler = new ThrowingHandler() },
            new LoopbackConnection(new IPEndPoint(IPAddress.Loopback, 0)));
        var source = PooledValueTaskCompletionSource<RespVoid>.Create(false);
        var future = source.AsValueTask();
        var payload = new RespClient.Payload(client, source, ResponseType.Void, "PING", Environment.TickCount);
        var body = new RegressionFrames.Counter();
        var attributeLifetime = new RegressionFrames.Counter();
        var attribute = new RespAggregatesPool().Lease();
        attribute.Add(RegressionFrames.Create("%0\r\n", attributeLifetime));
        if (aggregate)
        {
            var response = new RespAggregatesPool().Lease();
            response.Add(RegressionFrames.Create("*0\r\n", body));
            payload.Complete(response, Environment.TickCount, attribute);
        }
        else
            payload.Complete(RegressionFrames.Create("+OK\r\n", body), Environment.TickCount, attribute);

        Assert.True(future.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => future.GetAwaiter().GetResult());
        Assert.Equal(1, body.Disposals);
        Assert.Equal(1, attributeLifetime.Disposals);
        var late = new RegressionFrames.Counter();
        payload.Complete(RegressionFrames.Create("+LATE\r\n", late), Environment.TickCount, null);
        Assert.Equal(1, late.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpirationScannerContinuesAfterCallbackAndReportingFailures(bool reportingThrows)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var handler = new ThrowingHandler { ReportingThrows = reportingThrows };
        using var client = new RespClient(new RespClientOptions { Handler = handler, TimeoutCheckInterval = TimeSpan.FromMilliseconds(1) },
            new LoopbackConnection(listener.LocalEndPoint!));
        var start = client.Start();
        using var server = await listener.AcceptAsync(deadline.Token);
        await start.WaitAsync(deadline.Token);

        var first = EnqueueExpired(client);
        await Assert.ThrowsAsync<TimeoutException>(() => first.AsTask().WaitAsync(deadline.Token));
        await handler.Reported.Task.WaitAsync(deadline.Token);
        var second = EnqueueExpired(client);
        var third = EnqueueExpired(client);
        await Assert.ThrowsAsync<TimeoutException>(() => second.AsTask().WaitAsync(deadline.Token));
        await Assert.ThrowsAsync<TimeoutException>(() => third.AsTask().WaitAsync(deadline.Token));
    }

    private static ValueTask<RespVoid> EnqueueExpired(RespClient client)
    {
        var source = PooledValueTaskCompletionSource<RespVoid>.Create(false);
        var future = source.AsValueTask();
        var start = Environment.TickCount - 10;
        var payload = new RespClient.Payload(client, source, ResponseType.Void, "PING", start, timeout: 1);
        Assert.True(payload.OnAboutToWrite(1, start));
        return future;
    }

    private sealed class ThrowingHandler : TestHandler, IRespClientHandler
    {
        public bool ReportingThrows { get; init; }
        public TaskCompletionSource Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void IRespClientHandler.OnCommandExecuted(IRespClient client, int elapsedTimeInMs, string command, object? context)
            => throw new InvalidOperationException("Executed callback failed.");

        void IRespClientHandler.OnCommandTimedOut(IRespClient client, int elapsedTimeInMs, string command, object? context)
            => throw new InvalidOperationException("Timeout callback failed.");

        void IRespClientHandler.OnConnectionError(IRespClient client, Exception exception)
        {
            Reported.TrySetResult();
            if (ReportingThrows)
                throw new InvalidOperationException("Error reporting failed.");
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
}
