using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
    private int _currentGen;

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
            new DefaultConnectionProcedure(new ConnectionOptions
            {
                AuthOptions = new AuthOptions { Password = fixture.GetPassword() },
                Endpoint = fixture.GetStringEndpoint()
            }));

        await SubscribeToChannels(client);

        await client.Publish(Key.Utf8("greetings"), Value.Utf8("hello"));
        await client.Publish(Key.Utf8("greetings"), Value.Utf8("привет"));
        await client.Publish(Key.Utf8("greetings"), Value.Utf8("こんにちは"));
        await client.Publish(Key.Utf8("ort"), 1);
        await client.Publish(Key.Utf8("ntv"), 4);

        await Task.Delay(1000);

        Assert.Equal(expected: ["hello", "привет", "こんにちは"], testPushReceiver.Received["greetings"]);
        Assert.Equal(expected: ["1"], testPushReceiver.Received["ort"]);
        Assert.Equal(expected: ["4"], testPushReceiver.Received["ntv"]);

        await fixture.Restart();

        await WaitUntilReconnected(gen: 2);

        await client.Publish(Key.Utf8("greetings"), Value.Utf8("и снова здравствуйте"));

        Assert.Equal(expected: ["hello", "привет", "こんにちは", "и снова здравствуйте"], testPushReceiver.Received["greetings"]);
    }

    private async ValueTask OnClientConnected(IRespClient client, int generation)
    {
        await SubscribeToChannels(client);
        _currentGen = generation;
    }

    private static async Task SubscribeToChannels(IRespClient client)
    {
        await client.Subscribe(channel: Key.Utf8("greetings"));
        await client.Subscribe([Key.Utf8("ort"), Key.Utf8("ntv")]);
    }

    private async Task WaitUntilReconnected(int gen)
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (_currentGen < gen)
            await Task.Delay(500, ct.Token);
    }

    private class TestPushReceiver : IRespPushReceiver
    {
        public readonly Dictionary<string, List<string>> Received = [];

        public void Receive(RespPush push)
        {
            using (push)
            {
                var channel = Encoding.UTF8.GetString(push.GetChannel().GetSpan());
                var data = push.GetPushMessage();

                ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(Received, channel, out _);
                list ??= [];
                list.Add(data.ToString(Encoding.UTF8)!);
            }
        }
    }
}