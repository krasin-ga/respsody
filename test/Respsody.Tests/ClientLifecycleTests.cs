using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Options;
using Respsody.Exceptions;
using Respsody.Network;
using Xunit;

namespace Respsody.Tests;

public class ClientLifecycleTests
{
    [Fact]
    public async Task DisposalStopsConcurrentStartupAndPreventsRestart()
    {
        var connection = new PendingConnection();
        using var client = new RespClient(new RespClientOptions(), connection);
        var first = client.Start();
        var second = client.Start();
        await connection.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        client.Dispose();
        client.Dispose();

        await Assert.ThrowsAsync<RespClientDisposedException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<RespClientDisposedException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
        await connection.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<RespClientDisposedException>(() => client.Start());
        Assert.Equal(1, connection.Attempts);
    }

    [Fact]
    public async Task DisposalBeforeStartupDoesNotOpenAConnection()
    {
        var connection = new PendingConnection();
        using var client = new RespClient(new RespClientOptions(), connection);
        client.Dispose();

        await Assert.ThrowsAsync<RespClientDisposedException>(() => client.Start());
        Assert.Equal(0, connection.Attempts);
    }

    private sealed class PendingConnection : IConnectionProcedure
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts;
        public IReadOnlyDictionary<string, string?> Config => new Dictionary<string, string?>();
        public TimeSpan Timeout => TimeSpan.FromSeconds(10);

        public async Task<ConnectedSocket> Connect(CancellationToken token)
        {
            Interlocked.Increment(ref Attempts);
            Started.TrySetResult();
            try
            {
                await Task.Delay(System.Threading.Timeout.Infinite, token);
                throw new InvalidOperationException();
            }
            finally
            {
                Stopped.TrySetResult();
            }
        }
    }
}
