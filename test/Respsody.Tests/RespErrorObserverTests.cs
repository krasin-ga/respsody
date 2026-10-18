using System;
using System.Collections.Generic;
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
using Respsody.Tests.Library;
using Xunit;

namespace Respsody.Tests;

public class RespErrorObserverTests
{
    [Theory]
    [InlineData("-ERR failed\r\n", false)]
    [InlineData("-ERR failed\r\n", true)]
    [InlineData("!10\r\nERR failed\r\n", false)]
    [InlineData("!10\r\nERR failed\r\n", true)]
    public async Task ObserverFailurePreservesReplyCompletionOrderAndOwnership(string errorFrame, bool diagnosticThrows)
    {
        var source = PooledValueTaskCompletionSource<RespString>.Create(true);
        var future = source.AsValueTask();
        var callbackFailure = new InvalidOperationException("observer failed");
        var observed = new List<RespErrorResponseException>();
        var diagnostics = new List<Exception>();
        using var client = new RespClient(new RespClientOptions
        {
            RespErrorObservers =
            [
                error =>
                {
                    observed.Add(error);
                    Assert.False(future.IsCompleted);
                    throw callbackFailure;
                },
                error =>
                {
                    observed.Add(error);
                    Assert.False(future.IsCompleted);
                }
            ],
            Handler = new TestHandler(onConnectionError: (_, error) =>
            {
                diagnostics.Add(error);
                if (diagnosticThrows)
                    throw new InvalidOperationException("diagnostic failed");
            })
        }, new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var attributeLifetime = new RegressionFrames.Counter();
        var errorLifetime = new RegressionFrames.Counter();
        var successLifetime = new RegressionFrames.Counter();
        var payload = new RespClient.Payload(client, source, ResponseType.String, "GET", Environment.TickCount);
        Assert.True(payload.OnAboutToWrite(1, Environment.TickCount));
        var followingSource = PooledValueTaskCompletionSource<RespString>.Create(true);
        var followingFuture = followingSource.AsValueTask();
        var followingPayload = new RespClient.Payload(client, followingSource, ResponseType.String, "GET", Environment.TickCount);
        Assert.True(followingPayload.OnAboutToWrite(1, Environment.TickCount));
        var frames = new ReadyFrames<RespContext>(framing);
        frames.Add(RegressionFrames.Create("|0\r\n", attributeLifetime));
        frames.Add(RegressionFrames.Create(errorFrame, errorLifetime, dataOffset: errorFrame[0] == '!' ? 5 : 1));
        frames.Add(RegressionFrames.Create("+OK\r\n", successLifetime));

        client.HandleIncoming(frames);

        var exception = await Assert.ThrowsAsync<RespErrorResponseException>(async () => await future);
        Assert.Equal("ERR failed", exception.Error);
        Assert.Equal(2, observed.Count);
        Assert.All(observed, error => Assert.Same(exception, error));
        Assert.Same(callbackFailure, Assert.Single(diagnostics));
        Assert.Equal(1, attributeLifetime.Disposals);
        Assert.Equal(1, errorLifetime.Disposals);
        using (var response = await followingFuture)
        {
            Assert.Equal("OK", response.ToString());
            Assert.Equal(0, successLifetime.Disposals);
        }
        Assert.Equal(1, successLifetime.Disposals);
    }
}
