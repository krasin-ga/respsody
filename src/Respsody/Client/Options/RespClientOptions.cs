using System.Buffers;
using System.Diagnostics.Contracts;
using Respsody.Client.Options.Callbacks;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;

namespace Respsody.Client.Options;

public sealed record RespClientOptions
{
    private const int Size128K = 128* 1024;

    public StructuredSocketOptions SocketOption { get; init; } = new()
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1)
    };

    public IReadOnlyDictionary<string, string>? ClientMetadata { get; init; }

    public IRespClientHandler? Handler { get; init; }
    public bool RunContinuationsAsynchronously { get; init; } = true;
    public MemoryBlocks MemoryBlocks { get; init; } = MemoryBlocks.Shared;
    public int OutgoingMemoryBlockSize { get; init; } = Size128K;
    public int IncomingMemoryBlockSize { get; init; } = Size128K;
    public ArrayPool<byte> ArrayPool { get; init; } = ArrayPool<byte>.Shared;
    public RespAggregatesPool RespAggregatesPool { get; init; } = RespAggregatesPool.Shared;
    public IReadOnlyList<ConnectionInitialization> ConnectionInitializations { get; init; } = [];
    public IReadOnlyList<OnRespError> RespErrorObservers { get; init; } = [];
    public TimeSpan TimeoutCheckInterval { get; init; } = TimeSpan.FromSeconds(1);
    public IRespPushReceiver PushReceiver { get; init; } = new NoOpRespPushReceiver();

    [Pure]
    public RespClientOptions WithAdditionalConnectionInitializations(params ConnectionInitialization[] initializations)
    {
        return this with
        {
            ConnectionInitializations = [.. ConnectionInitializations, .. initializations]
        };
    }

    [Pure]
    public RespClientOptions WithRespErrorObservers(params OnRespError[] observers)
    {
        return this with
        {
            RespErrorObservers = [.. RespErrorObservers, .. observers]
        };
    }
}