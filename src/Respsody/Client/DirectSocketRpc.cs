using System.Net.Sockets;
using System.Text;
using Respsody.Exceptions;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;
using Respsody.Resp.Parsing;

namespace Respsody.Client;

/// <summary>
/// Used only for post-connection initialization.
/// Cannot be used concurrently with other operations on the socket.
/// </summary>
public sealed class DirectSocketRpc: IDisposable
{
    private const int Size1K = 1024;
    private readonly MemoryBlocks _blocks;
    private readonly RespFrameAggregationStrategy _frameAggregationStrategy;
    private readonly SemaphoreSlim _semaphoreSlim = new(1);
    private readonly RespFraming _sequence;
    private readonly Socket _socket;

    public DirectSocketRpc(Socket socket)
    {
        _socket = socket;
        _blocks = MemoryBlocks.Shared;
        _sequence = new RespFraming(_blocks, receivingBlockSize: Size1K);
        _frameAggregationStrategy = new RespFrameAggregationStrategy(new RespAggregatesPool());
    }

    public async Task<RespValueVariant> Rpc(string command, Action<Command<RespResponse>> commandBuilder, CancellationToken token)
    {
        await _semaphoreSlim.WaitAsync(token);

        try
        {
            return await ExecuteRpc(command, commandBuilder, token);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private async Task<RespValueVariant> ExecuteRpc(string command, Action<Command<RespResponse>> commandBuilder, CancellationToken token)
    {
        using var cmd = Command<RespResponse>.GetCommand().Init(_blocks, Size1K, command, clusterMode: false, @unsafe: false);
        commandBuilder(cmd);
        cmd.FinalizeCommand();

        await _socket.SendAsync(cmd.OutgoingBuffer.AsSegmentList()).WaitAsync(token);

        while (!token.IsCancellationRequested)
        {
            using var block = _sequence.GetReceivingBlock();
            var writableMemory = block.GetWritableMemory();

            var bytesRead = await _socket.ReceiveAsync(
                writableMemory,
                SocketFlags.None,
                token);

            if (bytesRead == 0)
                throw new StructuredSocketDisconnectedException(
                    "Zero bytes read",
                    innerException: null
                );

            block.Advance(bytesRead);

            using var sequences = _sequence.Feed(block);

            foreach (var sliceMemory in sequences)
            {
                if (!_frameAggregationStrategy.Aggregate(
                        sliceMemory,
                        out var variant))
                    continue;

                cmd.OutgoingBuffer.FreeByDeliveryPipeline();
                return variant;
            }
        }

        cmd.OutgoingBuffer.FreeByDeliveryPipeline();
        throw new RespConnectionException($"Error getting reply for {command} command");
    }

    public void Dispose()
    {
        _sequence.Dispose();
    }
}