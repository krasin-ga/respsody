using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Tests.Library;
using Xunit;
using Xunit.Abstractions;

namespace Respsody.Tests;

public class ServerTests(GarnetFixture fixture, ITestOutputHelper @out) : IClassFixture<GarnetFixture>
{
    [Theory]
    [InlineData(1024 * 1024)]
    [InlineData(1024 * 1024 * 128)]
    public async Task ShouldSendLargeMessages(int messageSize)
    {
        var message = Enumerable.Range(0, messageSize).Select(i => (byte)i).ToArray();

        var client = await new RespClientFactory().Create(fixture.GetEndpoint());

        var key = new Key($"{nameof(ShouldSendLargeMessages)}_{messageSize}", Encoding.UTF8);
        await client.Set(key, new Value(message));

        using var respString = await client.Get(key);

        Assert.Equal(message.AsSpan(), respString.GetSpan());
    }

    [Fact]
    public async Task ShouldExecuteCommandComboWithTransaction()
    {
        var client = await new RespClientFactory().Create(fixture.GetEndpoint());

        var key = new Key(Guid.NewGuid().ToString(), Encoding.UTF8);

        await client.Set(key, Value.Utf8("1"));

        using var incr = await client.Incr(key);
        using var combo = await Combo.Build(client)
            .Cmd(r => r.MultiCommand())
            .Cmd(r => r.IncrVoidCommand(key)) // Void overload used to avoid processing the -QUEUED response
            .Cmd(r => r.ExecCommand())
            .Execute();

        using var incr2 = await client.Incr(key);

        Assert.Equal(expected: 4L, incr2.ToInt64());
    }

    [Fact]
    public async Task ShouldDisconnectOnIdle()
    {
        var garnet = GarnetFixture.CreateGarnet();

        var wasConnected = false;
        var wasDisconnected = false;

        await new RespClientFactory().Create(
            new RespClientOptions
            {
                Handler = new TestHandler(
                    output: @out,
                    onConnected: (_, _) =>
                                 {
                                     wasConnected = true;
                                     return ValueTask.CompletedTask;
                                 },
                    onDisconnected: (_, _, gen) => wasDisconnected = gen > 0)
            },
            new DefaultConnectionProcedure(new ConnectionOptions { Endpoint = garnet.EndPoint.ToString() }));

        await Task.Delay(100);

        Assert.True(wasConnected);

        garnet.Dispose();

        for (var i = 0; i <= 10; i++)
        {
            if (wasDisconnected)
                return;

            await Task.Delay(50);
        }

        Assert.True(wasDisconnected);
    }
}