using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class LateResponseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateScalarReleasesFrameAndAttributeWithoutCompletingReusedSource(bool cancel)
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var cancellation = new CancellationTokenSource();
        var source = PooledValueTaskCompletionSource<RespString>.Create(true);
        var future = source.AsValueTask();
        var payload = new RespClient.Payload(client, source, ResponseType.String, "GET", 0,
            timeout: cancel ? int.MaxValue : 0, cancellationToken: cancellation.Token);
        if (cancel)
            cancellation.Cancel();

        Assert.True(payload.TryExpire(1));
        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await future);
        else
            await Assert.ThrowsAsync<TimeoutException>(async () => await future);

        var nextSource = PooledValueTaskCompletionSource<RespString>.Create(true);
        var nextFuture = nextSource.AsValueTask();
        var next = new RespClient.Payload(client, nextSource, ResponseType.String, "GET", 1);
        var frameLifetime = new CountedLifetime();
        var attributeLifetime = new CountedLifetime();
        var attribute = new RespAggregate(new RespAggregatesPool());
        attribute.Add(new Frame<RespContext>(new RespContext { Type = RespType.Attribute },
            "|0\r\n"u8.ToArray(), attributeLifetime));

        payload.Complete(StringFrame("late", frameLifetime), 2, attribute);

        Assert.Equal(1, frameLifetime.Disposals);
        Assert.Equal(1, attributeLifetime.Disposals);
        Assert.False(nextFuture.IsCompleted);

        var nextLifetime = new CountedLifetime();
        next.Complete(StringFrame("next", nextLifetime), 2, null);
        using (var response = await nextFuture)
            Assert.Equal("next", response.ToString());
        Assert.Equal(1, nextLifetime.Disposals);
    }

    private static Frame<RespContext> StringFrame(string value, IDisposable lifetime)
        => new(new RespContext { Type = RespType.SimpleString, DataOffset = 1 },
            Encoding.UTF8.GetBytes($"+{value}\r\n"), lifetime);

    private sealed class CountedLifetime : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
}
