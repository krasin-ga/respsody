using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class ComboTests
{
    [Fact]
    public async Task ReusingAFluentBuilderDoesNotConsumeTheOutstandingReply()
    {
        using var client = new RecordingClient();
        var builder = Combo.Build(client).Cmd(static c => c.CreateCommand<RespNumber>("INCR"));
        var future = builder.Execute();

        Assert.Throws<ObjectDisposedException>(() => builder.Cmd(static c => c.CreateCommand<RespNumber>("INCR")));
        Assert.Throws<ObjectDisposedException>(() => builder.Execute());
        Assert.Equal(0, client.Sources.Single().Reads);
        using var response = await future;
        Assert.Equal(1, response.ToInt64());
        Assert.Equal(1, client.Sources.Single().Reads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task SuccessfulExecutionReturnsCommandsAndLeavesResponsesOwnedByCaller(int count)
    {
        using var client = new RecordingClient();
        using (await ExecuteBatch(client, count))
        {
            client.AssertBuffersReturned();
            Assert.All(client.Lifetimes, lifetime => Assert.Equal(0, lifetime.Disposals));
            Assert.All(client.Sources, source => Assert.Equal(1, source.Reads));
        }
        Assert.All(client.Lifetimes, lifetime => Assert.Equal(1, lifetime.Disposals));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task RejectedSubmissionReturnsCommandsAndObservesEveryFuture(int count)
    {
        var failure = new InvalidOperationException("Submission rejected");
        using var client = new RecordingClient { ExecutionFailure = failure };
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteBatch(client, count));

        Assert.Same(failure, thrown);
        Assert.Equal(count, client.Sources.Count);
        Assert.All(client.Sources, source => Assert.Equal(1, source.Reads));
        client.AssertBuffersReturned();
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    [InlineData(5, 2)]
    [InlineData(5, 4)]
    public async Task MemberFailureWaitsForAndDisposesEverySuccessfulReply(int count, int failedIndex)
    {
        var failure = new InvalidOperationException("Member failed");
        using var client = new RecordingClient { AutoComplete = false };
        var execution = ExecuteBatch(client, count);
        var lateIndex = (failedIndex + 1) % count;
        client.AssertPendingBuffersReadable();

        client.Complete(failedIndex, failure);
        for (var index = 0; index < count; index++)
            if (index != failedIndex && index != lateIndex)
                client.Complete(index);
        Assert.False(execution.IsCompleted);
        client.Complete(lateIndex);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(failure, thrown);
        Assert.Equal(count - 1, client.Lifetimes.Count);
        Assert.All(client.Lifetimes, lifetime => Assert.Equal(1, lifetime.Disposals));
        Assert.All(client.Sources, source => Assert.Equal(1, source.Reads));
        client.AssertBuffersReturned();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void PackingFailureReleasesEarlierCommandsAndConsumesTheirFutures(int failedAt, bool withArguments)
    {
        using var client = new RecordingClient { PackFailureAt = failedAt };
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (withArguments)
                Combo.Build(client)
                    .Cmd(1, static (c, arg) => c.CreateCommand<RespNumber>("INCR").Arg(arg))
                    .Cmd(2, static (c, arg) => c.CreateCommand<RespNumber>("INCR").Arg(arg))
                    .Cmd(3, static (c, arg) => c.CreateCommand<RespNumber>("INCR").Arg(arg))
                    .Cmd(4, static (c, arg) => c.CreateCommand<RespNumber>("INCR").Arg(arg))
                    .Cmd(5, static (c, arg) => c.CreateCommand<RespNumber>("INCR").Arg(arg));
            else
                Combo.Build(client)
                    .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
                    .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
                    .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
                    .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
                    .Cmd(static c => c.CreateCommand<RespNumber>("INCR"));
        });

        Assert.Equal(failedAt - 1, client.Sources.Count);
        Assert.All(client.Sources, source => Assert.Equal(1, source.Reads));
        client.AssertBuffersReturned();
    }

    [Fact]
    public void FactoryFailureAlsoCleansTheExistingFluentBatch()
    {
        using var client = new RecordingClient();
        var builder = Combo.Build(client)
            .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
            .Cmd(static c => c.CreateCommand<RespNumber>("INCR"));

        Assert.Throws<InvalidOperationException>(() => builder.Cmd<RespNumber>(static _ => throw new InvalidOperationException("Factory failed")));

        Assert.Equal(2, client.Sources.Count);
        Assert.All(client.Sources, source => Assert.Equal(1, source.Reads));
        client.AssertBuffersReturned();
    }

    [Fact]
    public async Task DisposingAnUnsubmittedComboReleasesDeliveryAndPreservesItsObservableFuture()
    {
        using var client = new RecordingClient();
        var combo = new Combo(client);
        var command = combo.CreateCommand(static c => c.CreateCommand<RespNumber>("INCR"));
        var copy = combo;
        combo.Dispose();
        copy.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await command.Future());
        Assert.Equal(1, client.Sources.Single().Reads);
        client.AssertBuffersReturned();
    }

    [Fact]
    public async Task InfersTypesForFourAndFiveFactoryCommands()
    {
        using var client = new RecordingClient();
        using var response = await Combo.Build(client)
            .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
            .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
            .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
            .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
            .Cmd(static c => c.CreateCommand<RespNumber>("INCR"))
            .Execute();

        Assert.Equal(5, client.Created);
        Assert.Equal(5, client.Executed);
        Assert.Equal(1, response.Value4.ToInt64());
        Assert.Equal(1, response.Value5.ToInt64());
    }

    [Fact]
    public async Task FifthArgumentCommandIsCreatedAndExecutedExactlyOnce()
    {
        using var client = new RecordingClient();
        using var response = await Combo.Build(client)
            .Cmd(1, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Cmd(2, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Cmd(3, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Cmd(4, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Cmd(5, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Execute();

        Assert.Equal(5, client.Created);
        Assert.Equal(5, client.Executed);
        Assert.Equal(1, client.Batches);
        Assert.Equal(1, response.Value5.ToInt64());
    }

    private static async Task<IDisposable> ExecuteBatch(RecordingClient client, int count)
    {
        var first = Combo.Build(client).Cmd(static c => c.CreateCommand<RespNumber>("INCR"));
        if (count == 1)
            return await first.Execute();
        var second = first.Cmd(static c => c.CreateCommand<RespNumber>("INCR"));
        if (count == 2)
            return await second.Execute();
        var third = second.Cmd(static c => c.CreateCommand<RespNumber>("INCR"));
        if (count == 3)
            return await third.Execute();
        var fourth = third.Cmd(static c => c.CreateCommand<RespNumber>("INCR"));
        if (count == 4)
            return await fourth.Execute();
        return await fourth.Cmd(static c => c.CreateCommand<RespNumber>("INCR")).Execute();
    }

    private sealed class RecordingClient : IDirectRespClient, IDisposable
    {
        private readonly MemoryBlocks _blocks = new();
        private readonly RespClient _client;
        private readonly List<TransmitUnit<RespClient.Payload>> _pending = [];
        private readonly List<OutgoingBuffer> _buffers = [];
        private Combo? _combo;
        public List<TrackedSource> Sources { get; } = [];
        public List<Lifetime> Lifetimes { get; } = [];
        public int Created { get; private set; }
        public int Executed { get; private set; }
        public int Batches { get; private set; }
        public bool AutoComplete { get; init; } = true;
        public Exception? ExecutionFailure { get; init; }
        public int PackFailureAt { get; init; }
        public IReadOnlyDictionary<string, string?> ConnectionConfig => _client.ConnectionConfig;

        public RecordingClient()
        {
            _client = new RespClient(new RespClientOptions { MemoryBlocks = _blocks }, new DefaultConnectionProcedure(new ConnectionOptions()));
        }

        public Command<T> CreateCommand<T>(string name, bool @unsafe = false) where T : IRespResponse
        {
            Created++;
            var command = _client.CreateCommand<T>(name, @unsafe);
            _buffers.Add(command.OutgoingBuffer);
            return command;
        }

        public ValueTask<T> ExecuteCommand<T>(Command<T> command, CancellationToken cancellationToken = default)
            where T : IRespResponse => throw new NotSupportedException();

        public ComboCommand<T> Pack<T>(Combo combo, Command<T> command, CancellationToken token = default) where T : IRespResponse
        {
            _combo = combo;
            if (Created == PackFailureAt)
                throw new InvalidOperationException("Packing rejected");
            command.FinalizeCommand();
            var source = new TrackedSource<T>();
            Sources.Add(source);
            var payload = new RespClient.Payload(_client, source, ResponseTypeMapping.GetResponseTypeEnum<T>(), "INCR", Environment.TickCount);
            var packed = new ComboCommand<T>(command, payload, source.Future());
            combo.AddPayload(packed.TransmitUnit);
            _pending.Add(packed.TransmitUnit);
            return packed;
        }

        public void Execute(Combo combo)
        {
            Batches++;
            if (ExecutionFailure is { } failure)
                throw failure;
            if (AutoComplete)
                for (var index = 0; index < _pending.Count; index++)
                    Complete(index);
        }

        public void Complete(int index, Exception? failure = null)
        {
            var unit = _pending[index];
            Executed++;
            if (failure is { })
                unit.Payload.CompleteWithException(failure, Environment.TickCount);
            else
            {
                var lifetime = new Lifetime();
                Lifetimes.Add(lifetime);
                unit.Payload.Complete(new Frame<RespContext>(new RespContext { Type = RespType.Number },
                    ":1\r\n"u8.ToArray(), lifetime), Environment.TickCount, null);
            }
            unit.Page.FreeByDeliveryPipeline();
        }

        public void AssertBuffersReturned()
        {
            var leased = new List<OutgoingBuffer>();
            try
            {
                for (var index = 0; index < _buffers.Count; index++)
                {
                    var buffer = _blocks.LeaseLinked(128, true, 16);
                    leased.Add(buffer);
                    buffer.Commit();
                    buffer.FreeByDeliveryPipeline();
                }
                Assert.True(_buffers.ToHashSet().SetEquals(leased));
            }
            finally
            {
                foreach (var buffer in leased)
                    buffer.FreeByOwner();
            }
        }

        public void AssertPendingBuffersReadable()
        {
            Span<byte> bytes = stackalloc byte[256];
            foreach (var buffer in _buffers)
            {
                Assert.True(buffer.TryCopyTo(bytes, out var written));
                Assert.True(written > 0);
            }
        }

        public void Dispose()
        {
            _combo?.Dispose();
            _client.Dispose();
        }
    }

    private sealed class Lifetime : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    private abstract class TrackedSource
    {
        public int Reads { get; protected set; }
    }

    private sealed class TrackedSource<T> : TrackedSource, IValueTaskSource<T>, ITaskCompletionSource
    {
        private ManualResetValueTaskSourceCore<T> _source = new() { RunContinuationsAsynchronously = true };
        public ValueTask<T> Future() => new(this, _source.Version);
        public void SetException(Exception exception) => _source.SetException(exception);
        public void SetResult<TValue>(TValue result) => _source.SetResult((T)(object)result!);
        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _source.OnCompleted(continuation, state, token, flags);
        public T GetResult(short token)
        {
            Reads++;
            return _source.GetResult(token);
        }
    }
}
