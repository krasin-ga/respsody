using System;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Exceptions;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;
using Respsody.Resp.Parsing;
using Xunit;

namespace Respsody.Tests;

public class AttributeOwnershipTests
{
    [Fact]
    public void DisconnectReleasesPendingAttributeExactlyOnce()
    {
        using var client = CreateClient();
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var lifetime = new RegressionFrames.Counter();
        var frames = new ReadyFrames<RespContext>(framing);
        frames.Add(RegressionFrames.Create("|0\r\n", lifetime));

        client.HandleIncoming(frames);
        Assert.Equal(0, lifetime.Disposals);
        client.OnDisconnected(null, 1);
        client.OnDisconnected(null, 2);
        Assert.Equal(1, lifetime.Disposals);
    }

    [Fact]
    public async Task ErrorReplyReleasesItsAttribute()
    {
        using var client = CreateClient();
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var attributeLifetime = new RegressionFrames.Counter();
        var errorLifetime = new RegressionFrames.Counter();
        var source = PooledValueTaskCompletionSource<RespString>.Create(true);
        var future = source.AsValueTask();
        var payload = new RespClient.Payload(client, source, ResponseType.String, "GET", Environment.TickCount);
        Assert.True(payload.OnAboutToWrite(1, Environment.TickCount));
        var frames = new ReadyFrames<RespContext>(framing);
        frames.Add(RegressionFrames.Create("|0\r\n", attributeLifetime));
        frames.Add(RegressionFrames.Create("-ERR failed\r\n", errorLifetime));

        client.HandleIncoming(frames);

        await Assert.ThrowsAsync<RespErrorResponseException>(async () => await future);
        Assert.Equal(1, attributeLifetime.Disposals);
        Assert.Equal(1, errorLifetime.Disposals);
        client.OnDisconnected(null, 1);
        Assert.Equal(1, attributeLifetime.Disposals);
    }

    private static RespClient CreateClient() => new(new RespClientOptions(),
        new DefaultConnectionProcedure(new ConnectionOptions()));
}
