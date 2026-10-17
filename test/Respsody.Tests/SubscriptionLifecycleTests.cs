using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
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

public class SubscriptionLifecycleTests
{
    [Theory]
    [InlineData("SUBSCRIBE", "UNSUBSCRIBE")]
    [InlineData("PSUBSCRIBE", "PUNSUBSCRIBE")]
    [InlineData("SSUBSCRIBE", "SUNSUBSCRIBE")]
    public async Task ArgumentFreeUnsubscribeIsSerializedAndCompletesOnTheSocket(string subscribe, string unsubscribe)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pair = await ConnectionPair.Create(deadline.Token);
        var subscribed = SubscriptionCommand(pair.Client, subscribe, ["alpha"], default);
        var unsubscribed = SubscriptionCommand(pair.Client, unsubscribe, [], default);
        var ping = Ping(pair.Client);

        Assert.Equal(new[] { subscribe, "alpha" }, await pair.ReadCommand(deadline.Token));
        Assert.Equal(new[] { unsubscribe }, await pair.ReadCommand(deadline.Token));
        Assert.Equal(new[] { "PING" }, await pair.ReadCommand(deadline.Token));
        await pair.Write(Ack("alpha", subscribe) + Ack("alpha", unsubscribe, 0) + "+PONG\r\n", deadline.Token);

        using var subscribedAck = await subscribed.AsTask().WaitAsync(deadline.Token);
        using var unsubscribedAck = await unsubscribed.AsTask().WaitAsync(deadline.Token);
        Assert.Equal("alpha", Assert.Single(unsubscribedAck.Acks).ToString(Encoding.UTF8));
        using var pong = await ping.AsTask().WaitAsync(deadline.Token);
        Assert.Equal("PONG", pong.ToString());
    }

    [Theory]
    [InlineData("SUBSCRIBE", "UNSUBSCRIBE")]
    [InlineData("PSUBSCRIBE", "PUNSUBSCRIBE")]
    [InlineData("SSUBSCRIBE", "SUNSUBSCRIBE")]
    public async Task ArgumentFreeUnsubscribeResolvesChannelsAfterPipelinedSubscribe(string subscribe, string unsubscribe)
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var subscribed = QueueSubscription(client, subscribe, ["alpha", "beta"]);
        var unsubscribed = QueueSubscription(client, unsubscribe, []);
        var pingSource = PooledValueTaskCompletionSource<RespString>.Create(true);
        var ping = pingSource.AsValueTask();
        var pingPayload = new RespClient.Payload(client, pingSource, ResponseType.String, "PING", Environment.TickCount);
        Assert.True(pingPayload.OnAboutToWrite(1, Environment.TickCount));

        Feed(client, framing, Ack("alpha", subscribe) + Ack("beta", subscribe));
        using var subscribedAck = await subscribed;
        Assert.False(unsubscribed.IsCompleted);
        Feed(client, framing, Ack("alpha", unsubscribe));
        Assert.False(unsubscribed.IsCompleted);
        Feed(client, framing, Ack("beta", unsubscribe) + "+PONG\r\n");

        Assert.True(unsubscribed.IsCompletedSuccessfully);
        Assert.True(ping.IsCompletedSuccessfully);
        using var unsubscribedAck = await unsubscribed;
        Assert.Equal(new[] { "alpha", "beta" }, unsubscribedAck.Acks.Select(ack => ack.ToString(Encoding.UTF8)).Order());
        using var pong = await ping;
        Assert.Equal("PONG", pong.ToString());
    }

    [Theory]
    [InlineData("UNSUBSCRIBE", false)]
    [InlineData("PUNSUBSCRIBE", false)]
    [InlineData("SUNSUBSCRIBE", false)]
    [InlineData("UNSUBSCRIBE", true)]
    [InlineData("PUNSUBSCRIBE", true)]
    [InlineData("SUNSUBSCRIBE", true)]
    public async Task EmptyArgumentFreeUnsubscribeWaitsForItsOwnNullAcknowledgement(string unsubscribe, bool bulkNull)
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var first = QueueSubscription(client, unsubscribe, []);
        var second = QueueSubscription(client, unsubscribe, []);
        var reply = Ack(null, unsubscribe, 0);
        if (bulkNull)
            reply = reply.Replace("_\r\n", "$-1\r\n");
        Feed(client, framing, reply);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.False(second.IsCompleted);
        using var firstAck = await first;
        Assert.Empty(firstAck.Acks);
        Feed(client, framing, reply);
        Assert.True(second.IsCompletedSuccessfully);
        using var secondAck = await second;
        Assert.Empty(secondAck.Acks);
    }

    [Theory]
    [InlineData("SUBSCRIBE", "UNSUBSCRIBE")]
    [InlineData("PSUBSCRIBE", "PUNSUBSCRIBE")]
    [InlineData("SSUBSCRIBE", "SUNSUBSCRIBE")]
    public async Task ExplicitUnsubscribeUpdatesTheLaterArgumentFreeSnapshot(string subscribe, string unsubscribe)
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var subscribed = QueueSubscription(client, subscribe, ["alpha", "beta"]);
        var explicitRemoval = QueueSubscription(client, unsubscribe, ["alpha"]);
        var remaining = QueueSubscription(client, unsubscribe, []);
        Feed(client, framing, Ack("alpha", subscribe) + Ack("beta", subscribe) + Ack("alpha", unsubscribe));
        using var subscribedAck = await subscribed;
        using var explicitAck = await explicitRemoval;
        Assert.False(remaining.IsCompleted);
        Feed(client, framing, Ack("beta", unsubscribe, 0));
        Assert.True(remaining.IsCompletedSuccessfully);
        using var remainingAck = await remaining;
        Assert.Equal("beta", Assert.Single(remainingAck.Acks).ToString(Encoding.UTF8));
    }

    [Fact]
    public async Task ArgumentFreeUnsubscribeKeepsChannelPatternAndShardMembershipSeparate()
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var channels = QueueSubscription(client, "SUBSCRIBE", ["channel", "shared"]);
        var patterns = QueueSubscription(client, "PSUBSCRIBE", ["pattern*", "shared"]);
        var shards = QueueSubscription(client, "SSUBSCRIBE", ["shard", "shared"]);
        var removeChannels = QueueSubscription(client, "UNSUBSCRIBE", []);
        var removePatterns = QueueSubscription(client, "PUNSUBSCRIBE", []);
        var removeShards = QueueSubscription(client, "SUNSUBSCRIBE", []);
        Feed(client, framing, Ack("channel") + Ack("shared")
            + Ack("pattern*", "psubscribe") + Ack("shared", "psubscribe")
            + Ack("shard", "ssubscribe") + Ack("shared", "ssubscribe"));
        using var channelAck = await channels;
        using var patternAck = await patterns;
        using var shardAck = await shards;

        Feed(client, framing, Ack("channel", "unsubscribe", 3) + Ack("shared", "unsubscribe", 2));
        Assert.True(removeChannels.IsCompletedSuccessfully);
        Assert.False(removePatterns.IsCompleted);
        Assert.False(removeShards.IsCompleted);
        using var removedChannels = await removeChannels;
        Assert.Equal(new[] { "channel", "shared" }, removedChannels.Acks.Select(ack => ack.ToString(Encoding.UTF8)).Order());

        Feed(client, framing, Ack("pattern*", "punsubscribe", 1) + Ack("shared", "punsubscribe", 0));
        Assert.True(removePatterns.IsCompletedSuccessfully);
        Assert.False(removeShards.IsCompleted);
        using var removedPatterns = await removePatterns;
        Assert.Equal(new[] { "pattern*", "shared" }, removedPatterns.Acks.Select(ack => ack.ToString(Encoding.UTF8)).Order());

        Feed(client, framing, Ack("shard", "sunsubscribe", 1) + Ack("shared", "sunsubscribe", 0));
        Assert.True(removeShards.IsCompletedSuccessfully);
        using var removedShards = await removeShards;
        Assert.Equal(new[] { "shard", "shared" }, removedShards.Acks.Select(ack => ack.ToString(Encoding.UTF8)).Order());
    }

    [Fact]
    public async Task EmptyChannelIsDifferentFromAnEmptySubscriptionSet()
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var subscribed = QueueSubscription(client, "SUBSCRIBE", [""]);
        var unsubscribed = QueueSubscription(client, "UNSUBSCRIBE", []);
        var empty = QueueSubscription(client, "UNSUBSCRIBE", []);
        Feed(client, framing, Ack("") + Ack("", "unsubscribe", 0));
        using var subscribedAck = await subscribed;
        Assert.True(unsubscribed.IsCompletedSuccessfully);
        Assert.False(empty.IsCompleted);
        using var removed = await unsubscribed;
        Assert.Equal("", Assert.Single(removed.Acks).ToString(Encoding.UTF8));
        Feed(client, framing, Ack(null, "unsubscribe", 0));
        Assert.True(empty.IsCompletedSuccessfully);
        using var emptyAck = await empty;
        Assert.Empty(emptyAck.Acks);
    }

    [Fact]
    public async Task DisconnectClearsTheMembershipUsedByArgumentFreeUnsubscribe()
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var subscribed = QueueSubscription(client, "SUBSCRIBE", ["alpha"]);
        Feed(client, framing, Ack("alpha"));
        using var subscribedAck = await subscribed;
        client.OnDisconnected(null, 1);

        var unsubscribed = QueueSubscription(client, "UNSUBSCRIBE", [], generation: 2);
        Feed(client, framing, Ack(null, "unsubscribe", 0));
        Assert.True(unsubscribed.IsCompletedSuccessfully);
        using var acknowledgement = await unsubscribed;
        Assert.Empty(acknowledgement.Acks);
    }

    [Fact]
    public async Task DuplicateChannelsRequireOneAcknowledgementPerArgument()
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var firstSource = PooledValueTaskCompletionSource<RespSubscriptionAck>.Create(true);
        var first = firstSource.AsValueTask();
        var secondSource = PooledValueTaskCompletionSource<RespSubscriptionAck>.Create(true);
        var second = secondSource.AsValueTask();
        Assert.True(SubscriptionPayload(client, firstSource, ["alpha", "alpha"]).OnAboutToWrite(1, Environment.TickCount));
        Assert.True(SubscriptionPayload(client, secondSource, ["alpha"]).OnAboutToWrite(1, Environment.TickCount));

        Feed(client, framing, Ack("alpha"));
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Feed(client, framing, Ack("alpha"));
        Assert.True(first.IsCompletedSuccessfully);
        Assert.False(second.IsCompleted);
        using var firstAck = await first;
        Assert.Equal(2, firstAck.Acks.Length);
        Feed(client, framing, Ack("alpha"));
        Assert.True(second.IsCompletedSuccessfully);
        using var secondAck = await second;
        Assert.Single(secondAck.Acks);
    }

    [Fact]
    public async Task PreCanceledSubscriptionIsNotSentAndNextReplyStaysAligned()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pair = await ConnectionPair.Create(deadline.Token);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var subscription = Subscribe(pair.Client, ["alpha"], canceled.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await subscription.AsTask().WaitAsync(deadline.Token));

        var ping = Ping(pair.Client);
        Assert.Equal(new[] { "PING" }, await pair.ReadCommand(deadline.Token));
        await pair.Write("+PONG\r\n", deadline.Token);
        using var response = await ping.AsTask().WaitAsync(deadline.Token);
        Assert.Equal("PONG", response.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PartialAcknowledgementsRemainCancelableAndLateRepliesDoNotCompleteTheNextSubscription(bool cancel)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var probe = new ProbeReceiver();
        using var pair = await ConnectionPair.Create(deadline.Token, probe);
        using var cancellation = new CancellationTokenSource();
        var subscription = Subscribe(pair.Client, ["alpha", "beta"], cancellation.Token, cancel ? int.MaxValue : 1000);
        Assert.Equal(new[] { "SUBSCRIBE", "alpha", "beta" }, await pair.ReadCommand(deadline.Token));
        await pair.Write(Ack("alpha") + ">2\r\n+probe\r\n+ready\r\n", deadline.Token);
        await probe.Wait(deadline.Token);
        Assert.False(subscription.IsCompleted);

        if (cancel)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await subscription.AsTask().WaitAsync(deadline.Token));
        }
        else
            await Assert.ThrowsAsync<TimeoutException>(async () => await subscription.AsTask().WaitAsync(deadline.Token));

        var next = Subscribe(pair.Client, ["alpha", "beta"], default);
        var ping = Ping(pair.Client);
        Assert.Equal(new[] { "SUBSCRIBE", "alpha", "beta" }, await pair.ReadCommand(deadline.Token));
        Assert.Equal(new[] { "PING" }, await pair.ReadCommand(deadline.Token));

        await pair.Write(Ack("beta") + ">2\r\n+probe\r\n+ready\r\n", deadline.Token);
        await probe.Wait(deadline.Token);
        Assert.False(next.IsCompleted);
        await pair.Write(Ack("alpha") + Ack("beta") + "+PONG\r\n", deadline.Token);

        using var acknowledgement = await next.AsTask().WaitAsync(deadline.Token);
        Assert.Equal(2, acknowledgement.Acks.Length);
        using var response = await ping.AsTask().WaitAsync(deadline.Token);
        Assert.Equal("PONG", response.ToString());
    }

    [Fact]
    public async Task ErrorReplyRemovesItsSubscriptionConfirmation()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pair = await ConnectionPair.Create(deadline.Token);
        var rejected = Subscribe(pair.Client, ["alpha"], default);
        await pair.ReadCommand(deadline.Token);
        await pair.Write("-ERR subscription denied\r\n", deadline.Token);
        await Assert.ThrowsAsync<RespErrorResponseException>(async () => await rejected.AsTask().WaitAsync(deadline.Token));

        var next = Subscribe(pair.Client, ["alpha"], default);
        var ping = Ping(pair.Client);
        await pair.ReadCommand(deadline.Token);
        await pair.ReadCommand(deadline.Token);
        await pair.Write(Ack("alpha") + "+PONG\r\n", deadline.Token);

        using var acknowledgement = await next.AsTask().WaitAsync(deadline.Token);
        Assert.Single(acknowledgement.Acks);
        using var response = await ping.AsTask().WaitAsync(deadline.Token);
        Assert.Equal("PONG", response.ToString());
    }

    [Fact]
    public async Task DisconnectClearsPartialAcknowledgementsBeforeTheNextGeneration()
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var oldSource = PooledValueTaskCompletionSource<RespSubscriptionAck>.Create(true);
        var oldFuture = oldSource.AsValueTask();
        var oldPayload = SubscriptionPayload(client, oldSource, ["alpha", "beta"]);
        Assert.True(oldPayload.OnAboutToWrite(1, Environment.TickCount));
        Feed(client, framing, Ack("alpha"));
        var failure = new IOException("Connection closed");

        client.OnDisconnected(failure, 1);
        var thrown = await Assert.ThrowsAsync<IOException>(async () => await oldFuture);
        Assert.Same(failure, thrown);

        var nextSource = PooledValueTaskCompletionSource<RespSubscriptionAck>.Create(true);
        var nextFuture = nextSource.AsValueTask();
        var nextPayload = SubscriptionPayload(client, nextSource, ["alpha", "beta"]);
        Assert.True(nextPayload.OnAboutToWrite(2, Environment.TickCount));
        Feed(client, framing, Ack("alpha") + Ack("beta"));
        Assert.True(nextFuture.IsCompletedSuccessfully);
        using var acknowledgement = await nextFuture;
        Assert.Equal(2, acknowledgement.Acks.Length);
    }

    [Fact]
    public async Task CompletedLaterConfirmationIsRemovedWhenEarlierAcknowledgementsFinish()
    {
        using var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        using var framing = new RespFraming(new MemoryBlocks(), 128);
        var firstSource = PooledValueTaskCompletionSource<RespSubscriptionAck>.Create(true);
        var first = firstSource.AsValueTask();
        var secondSource = PooledValueTaskCompletionSource<RespSubscriptionAck>.Create(true);
        var second = secondSource.AsValueTask();
        Assert.True(SubscriptionPayload(client, firstSource, ["alpha", "beta"]).OnAboutToWrite(1, Environment.TickCount));
        Assert.True(SubscriptionPayload(client, secondSource, ["gamma"]).OnAboutToWrite(1, Environment.TickCount));
        var pingSource = PooledValueTaskCompletionSource<RespString>.Create(true);
        var ping = pingSource.AsValueTask();
        var pingPayload = new RespClient.Payload(client, pingSource, ResponseType.String, "PING", Environment.TickCount);
        Assert.True(pingPayload.OnAboutToWrite(1, Environment.TickCount));

        Feed(client, framing, Ack("gamma"));
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully);
        using var secondAck = await second;
        Feed(client, framing, Ack("alpha") + Ack("beta") + "+PONG\r\n");

        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(ping.IsCompletedSuccessfully);
        using var firstAck = await first;
        using var pong = await ping;
        Assert.Equal("PONG", pong.ToString());
    }

    private static RespClient.Payload SubscriptionPayload(RespClient client,
        PooledValueTaskCompletionSource<RespSubscriptionAck> source, string[] channels)
        => new(client, source, ResponseType.Subscription, "SUBSCRIBE", Environment.TickCount,
            subAcks: channels.Select(channel => new Bytes(Encoding.UTF8.GetBytes(channel))).ToArray());

    private static ValueTask<RespSubscriptionAck> QueueSubscription(RespClient client, string command, string[] channels,
        int generation = 1)
    {
        var source = PooledValueTaskCompletionSource<RespSubscriptionAck>.Create(true);
        var future = source.AsValueTask();
        var payload = new RespClient.Payload(client, source, ResponseType.Subscription, command, Environment.TickCount,
            subAcks: channels.Select(channel => new Bytes(Encoding.UTF8.GetBytes(channel))).ToArray());
        Assert.True(payload.OnAboutToWrite(generation, Environment.TickCount));
        return future;
    }

    private static void Feed(RespClient client, RespFraming framing, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text).AsSpan();
        while (!bytes.IsEmpty)
        {
            using var block = framing.GetReceivingBlock();
            var count = Math.Min(bytes.Length, block.Remaining);
            bytes[..count].CopyTo(block.GetWritableSpan());
            block.Advance(count);
            client.HandleIncoming(framing.Feed(block));
            bytes = bytes[count..];
        }
    }

    private static ValueTask<RespSubscriptionAck> Subscribe(RespClient client, string[] channels,
        CancellationToken token, int timeout = int.MaxValue)
        => SubscriptionCommand(client, "SUBSCRIBE", channels, token, timeout);

    private static ValueTask<RespSubscriptionAck> SubscriptionCommand(RespClient client, string name, string[] channels,
        CancellationToken token, int timeout = int.MaxValue)
    {
        using var command = client.CreateCommand<RespSubscriptionAck>(name).WithTimeout(timeout);
        foreach (var channel in channels)
            command.Token(channel);
        return client.ExecuteCommand(command, token);
    }

    private static ValueTask<RespString> Ping(RespClient client)
    {
        using var command = client.CreateCommand<RespString>("PING");
        return client.ExecuteCommand(command);
    }

    private static string Ack(string? channel, string command = "subscribe", int count = 1)
    {
        command = command.ToLowerInvariant();
        var channelFrame = channel is null ? "_\r\n" : $"${channel.Length}\r\n{channel}\r\n";
        return $">3\r\n${command.Length}\r\n{command}\r\n{channelFrame}:{count}\r\n";
    }

    private sealed class ProbeReceiver : IRespPushReceiver
    {
        private readonly Channel<bool> _received = Channel.CreateUnbounded<bool>();
        public ValueTask<bool> Wait(CancellationToken token) => _received.Reader.ReadAsync(token);
        public void Receive(RespPush push)
        {
            using (push)
                _received.Writer.TryWrite(true);
        }
    }

    private sealed class ConnectionPair(Socket listener, Socket server, RespClient client) : IDisposable
    {
        private readonly NetworkStream _stream = new(server, ownsSocket: false);
        private readonly StreamReader _reader = new(new NetworkStream(server, ownsSocket: false), Encoding.UTF8);
        public RespClient Client => client;

        public static async Task<ConnectionPair> Create(CancellationToken token, IRespPushReceiver? receiver = null)
        {
            var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var options = new RespClientOptions { TimeoutCheckInterval = TimeSpan.FromMilliseconds(10) };
            if (receiver is { })
                options = options with { PushReceiver = receiver };
            var client = new RespClient(options, new LoopbackConnection(listener.LocalEndPoint!));
            Socket? accepted = null;
            try
            {
                var start = client.Start();
                accepted = await listener.AcceptAsync(token);
                await start.WaitAsync(token);
                return new ConnectionPair(listener, accepted, client);
            }
            catch
            {
                client.Dispose();
                accepted?.Dispose();
                listener.Dispose();
                throw;
            }
        }

        public async Task<string[]> ReadCommand(CancellationToken token)
        {
            var header = await _reader.ReadLineAsync(token);
            Assert.NotNull(header);
            Assert.StartsWith("*", header);
            var arguments = new string[int.Parse(header[1..])];
            for (var index = 0; index < arguments.Length; index++)
            {
                var length = await _reader.ReadLineAsync(token);
                Assert.NotNull(length);
                Assert.StartsWith("$", length);
                arguments[index] = (await _reader.ReadLineAsync(token))!;
                Assert.Equal(int.Parse(length[1..]), Encoding.UTF8.GetByteCount(arguments[index]));
            }
            return arguments;
        }

        public ValueTask Write(string text, CancellationToken token)
            => _stream.WriteAsync(Encoding.UTF8.GetBytes(text), token);

        public void Dispose()
        {
            client.Dispose();
            _reader.Dispose();
            _stream.Dispose();
            server.Dispose();
            listener.Dispose();
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
