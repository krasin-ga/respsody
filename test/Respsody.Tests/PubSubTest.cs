using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Resp;
using Respsody.Tests.Library;
using Xunit;
using Xunit.Abstractions;

namespace Respsody.Tests;

public class PubSubTest(RedisSingleNodeFixture fixture, ITestOutputHelper @out) : IClassFixture<RedisSingleNodeFixture>
{
    private readonly TaskCompletionSource _subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task BasicPubSubTest()
    {
        var testPushReceiver = new TestPushReceiver();

        using var client = await new RespClientFactory().Create(
            new RespClientOptions
            {
                PushReceiver = testPushReceiver,
                Handler = new TestHandler(output: @out, onConnected: OnClientConnected),
            },
            fixture.CreateConnectionProcedure());

        await _subscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await client.Publish(Key.Utf8("greetings"), Value.Utf8("hello"));
        await client.Publish(Key.Utf8("greetings"), Value.Utf8("привет"));
        await client.Publish(Key.Utf8("greetings"), Value.Utf8("こんにちは"));
        await client.Publish(Key.Utf8("ort"), 1);
        await client.Publish(Key.Utf8("ntv"), 4);

        await testPushReceiver.AssertNext("greetings", "hello");
        await testPushReceiver.AssertNext("greetings", "привет");
        await testPushReceiver.AssertNext("greetings", "こんにちは");
        await testPushReceiver.AssertNext("ort", "1");
        await testPushReceiver.AssertNext("ntv", "4");

        await fixture.Restart();

        await _reconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await client.Publish(Key.Utf8("greetings"), Value.Utf8("и снова здравствуйте"));

        await testPushReceiver.AssertNext("greetings", "и снова здравствуйте");
    }

    private async ValueTask OnClientConnected(IRespClient client, int generation)
    {
        await SubscribeToChannels(client);
        _subscribed.TrySetResult();
        if (generation > 1)
            _reconnected.TrySetResult();
    }

    private static async Task SubscribeToChannels(IRespClient client)
    {
        await client.Subscribe(channel: Key.Utf8("greetings"));
        await client.Subscribe([Key.Utf8("ort"), Key.Utf8("ntv")]);
    }

    private class TestPushReceiver : IRespPushReceiver
    {
        private readonly Channel<(string Channel, string Message)> _received =
            Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

        public async Task AssertNext(string channel, string message)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.Equal((channel, message), await _received.Reader.ReadAsync(deadline.Token));
        }

        public void Receive(RespPush push)
        {
            using (push)
            {
                var channel = Encoding.UTF8.GetString(push.GetChannel().GetSpan());
                var data = push.GetPushMessage();

                Assert.True(_received.Writer.TryWrite((channel, data.ToString(Encoding.UTF8)!)));
            }
        }
    }
}
