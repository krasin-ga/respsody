using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
    private int _isStarted;
    public bool IsDisposed { get; private set; }
    public Task Connected => _initialConnectionTcs.Task;

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
        if (Interlocked.CompareExchange(ref _isStarted, 1, 0) != 0)
            return;

        _ = ReconnectionLoop();
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    private async Task ReconnectionLoop()
    {
        var connectionGeneration = 0;

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

                await _handler.InitializeConnection(connectedSocket, currentGeneration, linkedCts.Token);
                _initialConnectionTcs.TrySetResult();

                await disconnectedBeatCts.CancelAsync();
                await expirationCheck;

                var receivingTask = SafeRun(() => ReceivingLoop(socket, linkedCts.Token));
                var sendingTask = SafeRun(() => SendingLoop(socket, currentGeneration, linkedCts.Token));
                if (await SafeRun(() => _handler.OnConnected(currentGeneration, linkedCts.Token).AsTask()) is { } connectionHandlerException)
                {
                    _handler.HandleConnectionError(new RespOnConnectedCallbackException(connectionHandlerException));
                    await linkedCts.CancelAsync();
                }

                var interruptionException = await await Task.WhenAny(receivingTask, sendingTask);
                await linkedCts.CancelAsync();

                var whenAll = Task.WhenAll(receivingTask, sendingTask);
                while (!whenAll.IsCompleted)
                {
                    //signal noop to channel
                    _channelWriter.TryWrite(default);
                    await Task.Yield();
                }

                foreach (var exception in await whenAll)
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

    private async Task SendingLoop(Socket socket, int connectionGen, CancellationToken token)
    {
        var outgoingChannel = _outgoingChannel.Reader;
        var outgoingBuffer = new byte[_options.OutgoingBufferSize];
        var written = 0;
        
        while (await outgoingChannel.WaitToReadAsync(CancellationToken.None))
        {
            token.ThrowIfCancellationRequested();

            var ticks = Environment.TickCount;
            while (outgoingChannel.TryRead(out var data))
            {
                var (single, multiple) = data;

                if (single.HasValue)
                {
                    var (buffer, payload) = single.Value;
                    if (payload?.OnAboutToWrite(connectionGen, ticks) is false)
                    {
                        buffer.FreeByDeliveryPipeline();
                        continue;
                    }

                    if (buffer.TryCopyTo(outgoingBuffer.AsSpan(written), out var bytesWritten))
                    {
                        buffer.FreeByDeliveryPipeline();
                        written += bytesWritten;

                        continue;
                    }

                    if (written > 0)
                    {
                        await SendInternal(socket, outgoingBuffer, written, token);
                        ticks = Environment.TickCount;
                        written = 0;
                    }

                    if (buffer.TryCopyTo(outgoingBuffer.AsSpan(written), out bytesWritten))
                    {
                        buffer.FreeByDeliveryPipeline();
                        written += bytesWritten;
                        continue;
                    }

                    await SendInternal(socket, buffer);
                    ticks = Environment.TickCount;
                    continue;
                }

                if (multiple is { })
                {
                    foreach (var transmitUnit in multiple)
                    {
                        var (buffer, payload) = transmitUnit;
                        if (payload?.OnAboutToWrite(connectionGen, ticks) is false)
                        {
                            buffer.FreeByDeliveryPipeline();
                            continue;
                        }

                        if (buffer.TryCopyTo(outgoingBuffer.AsSpan(written), out var bytesWritten))
                        {
                            buffer.FreeByDeliveryPipeline();
                            written += bytesWritten;

                            continue;
                        }

                        if (written > 0)
                        {
                            await SendInternal(socket, outgoingBuffer, written, token);
                            written = 0;
                        }

                        if (buffer.TryCopyTo(outgoingBuffer.AsSpan(written), out bytesWritten))
                        {
                            buffer.FreeByDeliveryPipeline();
                            written += bytesWritten;
                            continue;
                        }

                        await SendInternal(socket, buffer);
                        ticks = Environment.TickCount;
                    }
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
        await socket.SendAsync(
            bytes.AsMemory(0, length),
            _options.SendingFlags,
            token);
    }

    private async ValueTask SendInternal(
        Socket socket,
        OutgoingBuffer page)
    {
        try
        {
            await socket.SendAsync(page.AsSegmentList(), _options.SendingFlags);
        }
        finally
        {
            page.FreeByDeliveryPipeline();
        }
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
        var writer = _outgoingChannel.Writer;

        var alive = new List<Outgoing>();
        while (await periodicTimer.WaitForNextTickAsync(token))
        {
            var tickCount = Environment.TickCount;
            while (reader.TryRead(out var data))
            {
                if (data.Single is { } single)
                {
                    if (single.Payload?.TryExpire(tickCount) is true)
                    {
                        single.Page.FreeByDeliveryPipeline();
                        continue;
                    }

                    alive.Add(data);
                }

                if (data.Batch is not { } multiple)
                    continue;

                if (multiple[0].Payload?.TryExpire(tickCount) is not true)
                    continue;

                multiple[0].Page.FreeByDeliveryPipeline();

                for (var i = 1; i < multiple.Count; i++)
                {
                    multiple[i].Payload?.LinkedCancel(tickCount);
                    multiple[i].Page.FreeByDeliveryPipeline();
                }
            }

            foreach (var data in alive)
                writer.TryWrite(data);

            alive.Clear();
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
        _channelWriter.Complete();
        _cts.Cancel();
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