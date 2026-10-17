using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Respsody.Exceptions;
using Respsody.Memory;

namespace Respsody.Network;

public sealed class StructuredSocket<TIncoming, TPayload> : IDisposable
    where TPayload : IPayload
{
    private readonly ChannelWriter<Outgoing> _channelWriter;
    private readonly IConnectionProcedure _connectionProcedure;
    private readonly CancellationTokenSource _cts = new();
    private readonly Framing<TIncoming> _framing;
    private readonly IStructuredSocketHandler<TIncoming> _handler;
    private readonly TaskCompletionSource _initialConnectionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StructuredSocketOptions _options;
    private readonly Channel<Outgoing> _outgoingChannel;
    private readonly Queue<Outgoing> _pendingOutgoing = new();
    private int _state;
    public bool IsDisposed => Volatile.Read(ref _state) == 2;
    public Task Connected => _initialConnectionTcs.Task;
    internal Task Completion { get; private set; } = Task.CompletedTask;

    public StructuredSocket(
        IConnectionProcedure connectionProcedure,
        Framing<TIncoming> framing,
        StructuredSocketOptions options,
        IStructuredSocketHandler<TIncoming> handler)
    {
        _connectionProcedure = connectionProcedure;
        _framing = framing;
        _options = options;
        _handler = handler;

        _outgoingChannel = Channel.CreateUnbounded<Outgoing>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        _channelWriter = _outgoingChannel.Writer;
    }

    public void Send(in TransmitUnit<TPayload> transmitUnit)
    {
        if (!_channelWriter.TryWrite(new Outgoing(transmitUnit)))
            throw new StructuredSocketDisconnectedException("Channel closed ", null);
    }

    public void Send(IReadOnlyList<TransmitUnit<TPayload>> batch)
    {
        if (!_channelWriter.TryWrite(new Outgoing(Batch: batch)))
            throw new StructuredSocketDisconnectedException("Channel closed ", null);
    }

    public void Start()
    {
        var state = Interlocked.CompareExchange(ref _state, 1, 0);
        ObjectDisposedException.ThrowIf(state == 2, this);
        if (state != 0)
            return;

        Completion = ReconnectionLoop();
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    private async Task ReconnectionLoop()
    {
        await Task.Yield();
        var connectionGeneration = 0;

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    _framing.Reset();
                    var currentGeneration = ++connectionGeneration;
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    using var disconnectedBeatCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

                    var expirationCheck = SafeRun(() => HeartbeatLoop(disconnectedBeatCts.Token));

                    ConnectedSocket connectedSocket;
                    try
                    {
                        connectedSocket = await _connectionProcedure.Connect(linkedCts.Token);
                    }
                    catch (Exception)
                    {
                        await disconnectedBeatCts.CancelAsync();
                        await expirationCheck;
                        throw;
                    }

                    using var socket = connectedSocket.Socket;
                    using var cancellation = linkedCts.Token.UnsafeRegister(static state => ((Socket)state!).Dispose(), socket);

                    try
                    {
                        await _handler.InitializeConnection(connectedSocket, currentGeneration, linkedCts.Token);
                        _initialConnectionTcs.TrySetResult();
                    }
                    finally
                    {
                        await disconnectedBeatCts.CancelAsync();
                        await expirationCheck;
                    }

                    var receivingTask = SafeRun(() => ReceivingLoop(socket, linkedCts.Token));
                    var sendingTask = SafeRun(() => SendingLoop(socket, currentGeneration, linkedCts.Token));
                    if (await SafeRun(() => _handler.OnConnected(currentGeneration, linkedCts.Token).AsTask()) is { } connectionHandlerException)
                    {
                        _handler.HandleConnectionError(new RespOnConnectedCallbackException(connectionHandlerException));
                        await linkedCts.CancelAsync();
                    }

                    var interruptionException = await await Task.WhenAny(receivingTask, sendingTask);
                    await linkedCts.CancelAsync();

                    foreach (var exception in await Task.WhenAll(receivingTask, sendingTask))
                        interruptionException ??= exception;

                    if (interruptionException is { })
                        _handler.OnDisconnected(interruptionException, currentGeneration);
                }
                catch (Exception exception)
                {
                    _handler.HandleConnectionError(exception);
                }
            }
        }
        finally
        {
            try
            {
                ReleaseQueued();
            }
            finally
            {
                _framing.Dispose();
            }
        }
    }

    private async Task SendingLoop(Socket socket, int connectionGen, CancellationToken token)
    {
        var outgoingChannel = _outgoingChannel.Reader;
        var outgoingBuffer = new byte[_options.OutgoingBufferSize];
        var written = 0;

        while (_pendingOutgoing.Count > 0 || await outgoingChannel.WaitToReadAsync(token))
        {
            token.ThrowIfCancellationRequested();
            var ticks = Environment.TickCount;
            while (!token.IsCancellationRequested &&
                   (_pendingOutgoing.TryDequeue(out var data) || outgoingChannel.TryRead(out data)))
            {
                var (single, multiple) = data;
                if (single.HasValue)
                {
                    var (buffer, payload) = single.Value;
                    try
                    {
                        if (payload?.OnAboutToWrite(connectionGen, ticks) is false)
                            continue;

                        if (buffer.TryCopyTo(outgoingBuffer.AsSpan(written), out var bytesWritten))
                        {
                            written += bytesWritten;
                            continue;
                        }

                        if (written > 0)
                        {
                            await SendInternal(socket, outgoingBuffer, written, token);
                            ticks = Environment.TickCount;
                            written = 0;
                        }

                        if (buffer.TryCopyTo(outgoingBuffer, out bytesWritten))
                        {
                            written = bytesWritten;
                            continue;
                        }

                        await SendInternal(socket, buffer);
                        ticks = Environment.TickCount;
                    }
                    finally
                    {
                        buffer.FreeByDeliveryPipeline();
                    }
                    continue;
                }

                if (multiple is not { })
                    continue;

                var index = 0;
                try
                {
                    for (; index < multiple.Count; index++)
                    {
                        var (buffer, payload) = multiple[index];
                        try
                        {
                            if (payload?.OnAboutToWrite(connectionGen, ticks) is false)
                                continue;

                            if (buffer.TryCopyTo(outgoingBuffer.AsSpan(written), out var bytesWritten))
                            {
                                written += bytesWritten;
                                continue;
                            }

                            if (written > 0)
                            {
                                await SendInternal(socket, outgoingBuffer, written, token);
                                ticks = Environment.TickCount;
                                written = 0;
                            }

                            if (buffer.TryCopyTo(outgoingBuffer, out bytesWritten))
                            {
                                written = bytesWritten;
                                continue;
                            }

                            await SendInternal(socket, buffer);
                            ticks = Environment.TickCount;
                        }
                        finally
                        {
                            buffer.FreeByDeliveryPipeline();
                        }
                    }
                }
                finally
                {
                    Release(multiple, index + 1, Environment.TickCount);
                }
            }

            if (written <= 0)
                continue;
            await SendInternal(socket, outgoingBuffer, written, token);
            written = 0;
        }
    }

    private async ValueTask SendInternal(
        Socket socket,
        byte[] bytes,
        int length,
        CancellationToken token)
    {
        await SocketWriter.SendAllAsync(socket,
            bytes.AsMemory(0, length),
            _options.SendingFlags,
            token);
    }

    private async ValueTask SendInternal(
        Socket socket,
        OutgoingBuffer page)
    {
        await SocketWriter.SendAllAsync(socket, page.AsSegmentList(), _options.SendingFlags);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private async Task ReceivingLoop(Socket socket, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var block = _framing.GetReceivingBlock();
            var writableMemory = block.GetWritableMemory();

            var bytesRead = await socket.ReceiveAsync(
                writableMemory,
                _options.ReceivingFlags,
                token);

            if (bytesRead == 0)
                throw new StructuredSocketDisconnectedException(
                    "Connection closed",
                    innerException: null
                );

            block.Advance(bytesRead);

            var sequences = _framing.Feed(block);

            if (sequences.IsEmpty)
            {
                sequences.Dispose();
                continue;
            }

            _handler.HandleIncoming(sequences);
        }
    }

    private async Task HeartbeatLoop(CancellationToken token)
    {
        using var periodicTimer = new PeriodicTimer(_options.HeartbeatInterval);

        var reader = _outgoingChannel.Reader;
        while (await periodicTimer.WaitForNextTickAsync(token))
        {
            var tickCount = Environment.TickCount;
            var pendingCount = _pendingOutgoing.Count;
            for (var i = 0; i < pendingCount; i++)
            {
                var pending = _pendingOutgoing.Dequeue();
                if (!TryExpire(pending, tickCount))
                    _pendingOutgoing.Enqueue(pending);
            }

            while (reader.TryRead(out var data))
            {
                if (!TryExpire(data, tickCount))
                    _pendingOutgoing.Enqueue(data);
            }
        }
    }

    private static bool TryExpire(in Outgoing data, int ticks)
    {
        if (data.Single is { } single)
        {
            try
            {
                if (single.Payload?.TryExpire(ticks) is not true)
                    return false;
            }
            catch
            {
                Release(single, ticks);
                throw;
            }
            single.Page.FreeByDeliveryPipeline();
            return true;
        }

        if (data.Batch is not { Count: > 0 } multiple)
            return true;

        try
        {
            if (multiple[0].Payload?.TryExpire(ticks) is not true)
                return false;
        }
        catch
        {
            Release(multiple, 0, ticks);
            throw;
        }

        multiple[0].Page.FreeByDeliveryPipeline();
        Release(multiple, 1, ticks);
        return true;
    }

    public void Dispose()
    {
        var state = Interlocked.Exchange(ref _state, 2);
        if (state == 2)
            return;
        _channelWriter.TryComplete();
        _cts.Cancel();
        _initialConnectionTcs.TrySetCanceled();
        if (state == 0)
        {
            try
            {
                ReleaseQueued();
            }
            finally
            {
                _framing.Dispose();
            }
        }
    }

    private void ReleaseQueued()
    {
        var ticks = Environment.TickCount;
        Exception? failure = null;
        while (_pendingOutgoing.TryDequeue(out var outgoing) || _outgoingChannel.Reader.TryRead(out outgoing))
        {
            try
            {
                Release(outgoing, ticks);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }

    private static void Release(in Outgoing outgoing, int ticks)
    {
        if (outgoing.Single is { } single)
            Release(single, ticks);
        else if (outgoing.Batch is { } batch)
            Release(batch, 0, ticks);
    }

    private static void Release(IReadOnlyList<TransmitUnit<TPayload>> batch, int start, int ticks)
    {
        Exception? failure = null;
        for (var i = start; i < batch.Count; i++)
        {
            try
            {
                Release(batch[i], ticks);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }

    private static void Release(in TransmitUnit<TPayload> unit, int ticks)
    {
        try
        {
            unit.Payload?.LinkedCancel(ticks);
        }
        finally
        {
            unit.Page.FreeByDeliveryPipeline();
        }
    }

    private static async Task<Exception?> SafeRun(Func<Task> taskFactory)
    {
        await Task.Yield();

        try
        {
            await taskFactory();
        }
        catch (Exception exception)
        {
            return exception;
        }

        return null;
    }

    private readonly record struct Outgoing(
        TransmitUnit<TPayload>? Single = null,
        IReadOnlyList<TransmitUnit<TPayload>>? Batch = null);
}
