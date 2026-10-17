using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Respsody.Client.Options;
using Respsody.Exceptions;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;
using Respsody.Resp.Parsing;

namespace Respsody.Client;

public sealed class RespClient(
    RespClientOptions options,
    IConnectionProcedure connectionProcedure)
    : IDirectRespClient, IStructuredSocketHandler<RespContext>, IDisposable
{
    private readonly IRespClientHandler? _clientHandler = options.Handler;
    private readonly object _initialConnectionLock = new();
    private volatile bool _disposed;
    private readonly MemoryBlocks _memoryBlocks = options.MemoryBlocks;
    private readonly int _outgoingBlockSize = options.OutgoingMemoryBlockSize;
    private readonly IRespPushReceiver _pushReceiver = options.PushReceiver;
    private readonly RespFrameAggregationStrategy _respFrameAggregationStrategy = new(options.RespAggregatesPool);
    private readonly ConcurrentQueue<Payload> _responseQueue = new();
    private readonly bool _runContinuationsAsynchronously = options.RunContinuationsAsynchronously;
    private readonly ConcurrentQueue<SubUnSub> _subUnSubConfirmationsQueue = new();
    private RespAggregate? _attribute;
    private GCHandle? _handlersHandle;
    private StructuredSocket<RespContext, Payload>? _structuredSocket;
    private readonly DisposalGuard _dummyGuard = new(new CompletionGuard(), checkOnly: true);
    public ConnectionMetadata? Metadata { get; private set; }
    public IReadOnlyDictionary<string, string?> ConnectionConfig => connectionProcedure.Config;

    /// <summary>
    /// Explicitly starts the connection and waits up to <see cref="IConnectionProcedure.Timeout"/> for it to complete.
    /// </summary>
    public async Task Start()
    {
        using var cts = new CancellationTokenSource();
        if (connectionProcedure.Timeout > TimeSpan.Zero)
            cts.CancelAfter(connectionProcedure.Timeout);

        try
        {
            lock (_initialConnectionLock)
            {
                if (_disposed)
                    throw new RespClientDisposedException();

                if (_structuredSocket is null)
                {
                    _structuredSocket = new StructuredSocket<RespContext, Payload>(
                        connectionProcedure,
                        new RespFraming(_memoryBlocks, options.IncomingMemoryBlockSize, options.ArrayPool),
                        options.SocketOption,
                        this
                    );

                    if (_clientHandler != null)
                        _handlersHandle = GCHandle.Alloc(
                            _memoryBlocks.AddEventHandlers(
                                onCreate: _clientHandler.OnMemoryBlockCreated,
                                onDestruct: _clientHandler.OnMemoryBlockDestructed,
                                onResurrected: _clientHandler.OnMemoryBlockResurrected
                            ),
                            GCHandleType.Normal);
                    _structuredSocket.Start();
                    _ = CheckExpiredMessages();
                }
            }

            await _structuredSocket.Connected.WaitAsync(cts.Token);

        }
        catch (OperationCanceledException exception)
        {
            if (_disposed)
                throw new RespClientDisposedException();
            throw new TimeoutException($"Connection timed out after {connectionProcedure.Timeout}", exception);
        }
    }

    private async Task CheckExpiredMessages()
    {
        if (options.TimeoutCheckInterval <= TimeSpan.Zero)
            return;

        using var timer = new PeriodicTimer(options.TimeoutCheckInterval);

        while (await timer.WaitForNextTickAsync())
        {
            if (_structuredSocket?.IsDisposed is true)
                break;

            var ticks = Environment.TickCount;
            foreach (var responseData in _responseQueue)
            {
                try
                {
                    responseData.TryExpire(ticks);
                }
                catch (Exception exception)
                {
                    try
                    {
                        HandleConnectionError(exception);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    public Command<T> CreateCommand<T>(string command, bool @unsafe = false)
        where T : IRespResponse
    {
        return Command<T>.GetCommand().Init(
            _memoryBlocks,
            _outgoingBlockSize,
            command,
            clusterMode: false,
            @unsafe: @unsafe);
    }

    public ValueTask<T> ExecuteCommand<T>(
        Command<T> command,
        CancellationToken cancellationToken = default)
        where T : IRespResponse
    {
        command.FinalizeCommand();

        if (_structuredSocket is null)
            return ConnectThenExecuteCommand(command, cancellationToken);

        return ExecuteFinalizedCommand(command, cancellationToken);
    }

    private ValueTask<T> ExecuteFinalizedCommand<T>(Command<T> command, CancellationToken cancellationToken)
        where T : IRespResponse
    {
        if (_structuredSocket!.IsDisposed)
        {
            command.OutgoingBuffer.FreeByDeliveryPipeline();
            throw new RespClientDisposedException();
        }

        var taskCompletionSource = PooledValueTaskCompletionSource<T>.Create(
            _runContinuationsAsynchronously
        );

        try
        {
            Bytes[]? subAcks = null;
            var responseType = ResponseTypeMapping.GetResponseTypeEnum<T>();
            if (responseType == ResponseType.Subscription)
            {
                subAcks = CommandParser.ParseArgumentsSlow(command);
            }

            var payload = new Payload(
                respClient: this,
                taskCompletionSource: taskCompletionSource,
                responseType: responseType,
                command: command.CommandName ?? "<unknown>",
                startTicks: Environment.TickCount,
                timeout: command.Timeout,
                subAcks: subAcks,
                cancellationToken: cancellationToken);

            _structuredSocket.Send(new TransmitUnit<Payload>(command.OutgoingBuffer, payload));
        }
        catch (Exception exception)
        {
            command.OutgoingBuffer.FreeByDeliveryPipeline();
            taskCompletionSource.SetException(exception);
        }

        return taskCompletionSource.AsValueTask();
    }

    public ComboCommand<T> Pack<T>(Combo combo, Command<T> command, CancellationToken token = default)
        where T : IRespResponse
    {
        if (_disposed || _structuredSocket?.IsDisposed is true)
            throw new RespClientDisposedException();

        var responseType = ResponseTypeMapping.GetResponseTypeEnum<T>();
        if (responseType == ResponseType.Subscription)
            throw new InvalidOperationException("Cannot pack subscription");

        command.FinalizeCommand();

        var responseCompletionSource = PooledValueTaskCompletionSource<T>.Create(
            _runContinuationsAsynchronously
        );

        try
        {
            var payload = new Payload(
                respClient: this,
                taskCompletionSource: responseCompletionSource,
                responseType: responseType,
                command: "<combo>",
                timeout: command.Timeout,
                startTicks: Environment.TickCount,
                cancellationToken: token);

            var comboCommand = new ComboCommand<T>(command, payload, responseCompletionSource.AsValueTask());
            combo.AddPayload(comboCommand.TransmitUnit);
            return comboCommand;
        }
        catch
        {
            command.OutgoingBuffer.FreeByDeliveryPipeline();
            responseCompletionSource.ReturnUnused();
            throw;
        }
    }

    public void Execute(Combo combo)
    {
        if (_structuredSocket?.IsDisposed is true)
            throw new RespClientDisposedException();

        if (_structuredSocket is null)
            throw new Exception("Cannot execute combo on a disconnected socket");

        _structuredSocket.Send(combo.PreparedPayload);
    }

    private async ValueTask<TResponse> ConnectThenExecuteCommand<TResponse>(
        Command<TResponse> command,
        CancellationToken cancellationToken)
        where TResponse : IRespResponse
    {
        using var commandOwnership = command.WithOwnership();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (command.Timeout > 0)
            linkedCts.CancelAfter(command.Timeout);

        try
        {
            await Start().WaitAsync(linkedCts.Token);
        }
        catch
        {
            command.OutgoingBuffer.FreeByDeliveryPipeline();
            throw;
        }

        return await ExecuteFinalizedCommand(command, linkedCts.Token);
    }

    public void HandleIncoming(ReadyFrames<RespContext> readyFrames)
    {
        var ticks = Environment.TickCount;

        using (readyFrames)
        {
            foreach (var sliceMemory in readyFrames)
            {
                if (!_respFrameAggregationStrategy.Aggregate(
                        sliceMemory,
                        out var variant))
                    continue;

                if (variant.Simple is { } simple)
                {
                    HandleSimpleResponse(simple, ticks);
                    continue;
                }

                if (variant.Aggregate is { } aggregate)
                    HandleAggregate(aggregate, ticks);
            }
        }
    }

    private void HandleAggregate(RespAggregate aggregate, int ticks)
    {
        if (aggregate.Context.Type == RespType.Attribute)
        {
            _attribute = aggregate;
            return;
        }

        if (aggregate.HeaderFrame.Context.Type == RespType.Push)
        {
            if (aggregate.ToRespPush(_dummyGuard).TryGetSubscription(out var data))
            {
                foreach (var confirmation in _subUnSubConfirmationsQueue)
                {
                    if (confirmation.Handle(data.Command, data.Ack) is var res &&
                        res != SubUnSub.HandleResult.Unhandled)
                    {
                        if (res == SubUnSub.HandleResult.Completed)
                            DequeueCompletedSubscriptions();
                        break;
                    }
                }

                aggregate.Dispose();
                return;
            }

            _pushReceiver.Receive(new RespPush(aggregate, new CompletionGuard()));
            return;
        }

        if (!_responseQueue.TryDequeue(out var payload))
            Panic("[handle aggregate] failed to dequeue response for completion");

        var attribute = _attribute;
        _attribute = null;

        payload.Complete(aggregate, ticks, attribute);
    }

    private void HandleSimpleResponse(Frame<RespContext> frame, int ticks)
    {
        if (!_responseQueue.TryDequeue(out var payload))
            Panic("[handle simple response] failed to dequeue response for completion");

        var attribute = _attribute;
        _attribute = null;

        if (frame.Context.Type is RespType.BulkError or RespType.SimpleError)
        {
            if (_subUnSubConfirmationsQueue.TryPeek(out var confirmation)
                && payload.IsSubscription(confirmation.Command, confirmation.Acks))
            {
                _subUnSubConfirmationsQueue.TryDequeue(out _);
                DequeueCompletedSubscriptions();
            }
            attribute?.Dispose();
            using var errorString = new RespString(frame, payload.Guard);
            var exception = new RespErrorResponseException(errorString.ToString(Encoding.UTF8) ?? "NULL");
            NotifyRespError(exception);
            payload.CompleteWithException(exception, ticks);
            return;
        }

        payload.Complete(frame, ticks, attribute);
    }

    private void NotifyRespError(RespErrorResponseException exception)
    {
        foreach (var observer in options.RespErrorObservers)
        {
            try
            {
                observer(exception);
            }
            catch (Exception observerException)
            {
                try
                {
                    HandleConnectionError(observerException);
                }
                catch
                {
                }
            }
        }
    }

    private void DequeueCompletedSubscriptions()
    {
        while (_subUnSubConfirmationsQueue.TryPeek(out var confirmation) && confirmation.IsDone)
        {
            if (!_responseQueue.TryPeek(out var response)
                || !response.IsSubscription(confirmation.Command, confirmation.Acks))
                break;

            _responseQueue.TryDequeue(out _);
            _subUnSubConfirmationsQueue.TryDequeue(out _);
        }
    }

    public void OnDisconnected(Exception? exception, int generation)
    {
        Exception? failure = null;
        try
        {
            _clientHandler?.OnDisconnected(this, exception, generation);
        }
        catch (Exception callbackException)
        {
            failure = callbackException;
        }

        _respFrameAggregationStrategy.Reset();
        _attribute?.Dispose();
        _attribute = null;

        while (_responseQueue.TryPeek(out var response) && response.ConnectionGeneration <= generation)
        {
            if (!_responseQueue.TryDequeue(out _))
                Panic("[on disconnected] failed to dequeue response for completion with error");

            try
            {
                response.CompleteWithException(exception ?? new RespConnectionLostException(),
                    ticks: Environment.TickCount);
            }
            catch (Exception callbackException)
            {
                failure ??= callbackException;
            }
        }

        while (_subUnSubConfirmationsQueue.TryPeek(out var sub) && sub.ConnectionGeneration <= generation)
        {
            if (!_subUnSubConfirmationsQueue.TryDequeue(out _))
                Panic("[on disconnected] failed to dequeue sub for completion with error");

            sub.CompleteWithException(exception ?? new RespConnectionLostException());
        }

        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }

    public async ValueTask InitializeConnection(ConnectedSocket connectedSocket, int generation,
        CancellationToken cancellationToken)
    {
        Metadata = connectedSocket.Metadata;
        foreach (var initialization in options.ConnectionInitializations)
            await initialization(connectedSocket, cancellationToken);
    }

    public ValueTask OnConnected(int generation, CancellationToken cancellationToken)
    {
        return _clientHandler?.OnConnected(this, generation) ?? ValueTask.CompletedTask;
    }

    public void HandleConnectionError(Exception exception)
    {
        _clientHandler?.OnConnectionError(this, exception);
    }

    private void Panic(string reason)
    {
        var exception = new RespClientCorruptedStateException(reason);
        _clientHandler?.OnPanic(this, exception);

        try
        {
            while (_responseQueue.TryDequeue(out var responseCompletionData))
                responseCompletionData.CompleteWithException(exception, ticks: Environment.TickCount);

            _structuredSocket?.Dispose();
        }
        catch
        {
            //
        }

        throw exception;
    }

    public void Dispose()
    {
        lock (_initialConnectionLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _structuredSocket?.Dispose();
            _handlersHandle?.Free();
            _handlersHandle = null;
        }
    }

    internal struct Payload(
        RespClient respClient,
        ITaskCompletionSource taskCompletionSource,
        ResponseType responseType,
        string command,
        int startTicks,
        int timeout = int.MaxValue,
        Bytes[]? subAcks = null,
        CancellationToken cancellationToken = default)
        : IPayload
    {
        public int ConnectionGeneration { get; private set; }
        public CompletionGuard Guard => _ct;
        private readonly CompletionGuard _ct = new();
        private readonly object? _ctx = respClient._clientHandler?.CreateCommandContext(respClient, command);

        public bool OnAboutToWrite(int socketId, int ticks)
        {
            var elapsed = ticks - startTicks;
            if (elapsed > timeout)
            {
                InternalCompleteWithException(
                    new TimeoutException(
                        $"Command timed out before being sent to the server." +
                        $" Elapsed: {TimeSpan.FromTicks(elapsed)}" +
                        $" Timeout: {timeout}"));

                respClient._clientHandler?.OnCommandTimedOut(respClient, elapsed, command, _ctx);

                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                InternalCompleteWithException(
                    new OperationCanceledException("Command was cancelled before being sent to the server"));

                respClient._clientHandler?.OnCommandCancelled(respClient, elapsed, command, _ctx);

                return false;
            }

            //mutate before copying 
            ConnectionGeneration = socketId;
            var payload = this;
            respClient._responseQueue.Enqueue(payload);
            if (responseType == ResponseType.Subscription)
                respClient._subUnSubConfirmationsQueue.Enqueue(new SubUnSub(command, subAcks!, socketId, taskCompletionSource, _ct));

            // cancellationToken.Register(() =>
            //     payload.CompleteWithException(new OperationCanceledException("The command was cancelled"), Environment.TickCount));

            return true;
        }

        public bool TryExpire(int ticks)
        {
            var elapsed = ticks - startTicks;
            if (elapsed > timeout)
            {
                InternalCompleteWithException(
                    new TimeoutException(
                        $"The command expired before receiving a response. Timeout={timeout}ms, Elapsed={elapsed}ms")
                );

                respClient._clientHandler?.OnCommandTimedOut(respClient, elapsed, command, _ctx);

                return true;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                InternalCompleteWithException(
                    new OperationCanceledException("The command was canceled before receiving a response")
                );

                respClient._clientHandler?.OnCommandCancelled(respClient, elapsed, command, _ctx);

                return true;
            }

            return false;
        }

        public void LinkedCancel(int ticks)
        {
            CompleteWithException(
                new OperationCanceledException("The command was cancelled by first command in chain"),
                ticks
            );

            respClient._clientHandler?.OnCommandCancelled(respClient, ticks - startTicks, command, _ctx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CompleteWithException(Exception exception, int ticks)
        {
            if (_ct?.TryComplete() is false)
                return;

            taskCompletionSource.SetException(exception);
            respClient._clientHandler?.OnCommandFailed(respClient, ticks - startTicks, command, _ctx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalCompleteWithException(Exception exception)
        {
            if (_ct?.TryComplete() is false)
                return;

            taskCompletionSource.SetException(exception);
        }

        public void Complete(RespAggregate aggregate, int ticks, RespAggregate? attribute)
        {
            if (_ct?.TryComplete() is false)
            {
                aggregate.Dispose();
                attribute?.Dispose();
                return;
            }

            try
            {
                respClient._clientHandler?.OnCommandExecuted(respClient, ticks - startTicks, command, _ctx);
            }
            catch (Exception exception)
            {
                aggregate.Dispose();
                attribute?.Dispose();
                taskCompletionSource.SetException(exception);
                return;
            }

            switch (responseType)
            {
                case ResponseType.String:
                case ResponseType.Boolean:
                case ResponseType.Double:
                case ResponseType.Number:
                case ResponseType.BigNumber:
                    taskCompletionSource.SetException(new RespUnexpectedResponseException(responseType, aggregate));
                    aggregate.Dispose();
                    attribute?.Dispose();
                    break;
                case ResponseType.Array:
                    CompleteArrayResponse(aggregate);
                    attribute?.Dispose();
                    break;
                case ResponseType.Map:
                    CompleteMapResponse(aggregate);
                    attribute?.Dispose();
                    break;
                case ResponseType.Set:
                    CompleteSetResponse(aggregate);
                    attribute?.Dispose();
                    break;
                case ResponseType.Untyped:
                    taskCompletionSource.SetResult(
                        new RespResponse(
                            frame: null,
                            aggregate: aggregate,
                            attribute: attribute,
                            guard: Guard
                        ));
                    break;
                case ResponseType.Void:
                    taskCompletionSource.SetResult(new RespVoid());
                    aggregate.Dispose();
                    attribute?.Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Complete(Frame<RespContext> frame, int ticks, RespAggregate? attribute)
        {
            if (_ct?.TryComplete() is false)
            {
                frame.Dispose();
                attribute?.Dispose();
                return;
            }

            try
            {
                respClient._clientHandler?.OnCommandExecuted(respClient, ticks - startTicks, command, _ctx);
            }
            catch (Exception exception)
            {
                frame.Dispose();
                attribute?.Dispose();
                taskCompletionSource.SetException(exception);
                return;
            }

            switch (responseType)
            {
                case ResponseType.String:
                    CompleteStringResponse(frame);
                    attribute?.Dispose();
                    break;
                case ResponseType.Boolean:
                    CompleteBooleanResponse(frame);
                    attribute?.Dispose();
                    break;
                case ResponseType.Double:
                    CompleteDoubleResponse(frame);
                    attribute?.Dispose();
                    break;
                case ResponseType.Number:
                    CompleteNumberResponse(frame);
                    attribute?.Dispose();
                    break;
                case ResponseType.BigNumber:
                    CompleteBigNumberResponse(frame);
                    attribute?.Dispose();
                    break;
                case ResponseType.Array:
                case ResponseType.Map:
                case ResponseType.Set:
                    taskCompletionSource.SetException(new RespUnexpectedResponseException(responseType, frame));
                    frame.Dispose();
                    attribute?.Dispose();
                    break;
                case ResponseType.Untyped:
                    taskCompletionSource.SetResult(
                        new RespResponse(
                            frame: frame,
                            aggregate: null,
                            attribute: attribute,
                            guard: Guard
                        ));
                    break;
                case ResponseType.Void:
                    taskCompletionSource.SetResult(new RespVoid());
                    frame.Dispose();
                    attribute?.Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteSetResponse(RespAggregate aggregate)
        {
            if (!RespSet.CanConvert(aggregate.HeaderFrame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Set, aggregate));
                aggregate.Dispose();
                return;
            }

            taskCompletionSource.SetResult(new RespSet(aggregate, _ct));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteArrayResponse(RespAggregate aggregate)
        {
            if (!RespArray.CanConvert(aggregate.HeaderFrame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Array, aggregate));
                aggregate.Dispose();
                return;
            }

            taskCompletionSource.SetResult(new RespArray(aggregate, _ct));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteMapResponse(RespAggregate aggregate)
        {
            if (!RespMap.CanConvert(aggregate.HeaderFrame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Map, aggregate));
                aggregate.Dispose();
                return;
            }

            taskCompletionSource.SetResult(new RespMap(aggregate, _ct));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteStringResponse(Frame<RespContext> frame)
        {
            if (!RespString.CanConvert(frame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.String, frame));
                frame.Dispose();
                return;
            }

            taskCompletionSource.SetResult(new RespString(frame, _ct));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteNumberResponse(Frame<RespContext> frame)
        {
            if (!RespNumber.CanConvert(frame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Number, frame));
                frame.Dispose();
                return;
            }

            taskCompletionSource.SetResult(new RespNumber(frame, _ct));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteBigNumberResponse(Frame<RespContext> frame)
        {
            if (!RespBigNumber.CanConvert(frame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.BigNumber, frame));
                frame.Dispose();
                return;
            }

            taskCompletionSource.SetResult(new RespBigNumber(frame, _ct));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteDoubleResponse(Frame<RespContext> frame)
        {
            if (!RespDouble.CanConvert(frame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Double, frame));
                frame.Dispose();
                return;
            }

            taskCompletionSource.SetResult(new RespDouble(frame, _ct));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteBooleanResponse(Frame<RespContext> frame)
        {
            if (!RespBoolean.CanConvert(frame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Boolean, frame));
                frame.Dispose();
                return;
            }

            taskCompletionSource.SetResult(new RespBoolean(frame, _ct));
        }

        internal bool IsSubscription(string cmd, Bytes[] acks)
        {
            return responseType is ResponseType.Subscription
                   && string.Equals(cmd, command, StringComparison.OrdinalIgnoreCase)
                   && ReferenceEquals(acks, subAcks);
        }
    }

    private class SubUnSub(
        string command,
        Bytes[] acks,
        int connectionGeneration,
        ITaskCompletionSource taskCompletionSource,
        CompletionGuard ct)
    {
        public enum HandleResult
        {
            Unhandled,
            Handled,
            Completed
        }

        private readonly Dictionary<Bytes, int> _remainingAcks = CountAcknowledgements(acks);
        public string Command => command;
        public Bytes[] Acks => acks;
        public int ConnectionGeneration => connectionGeneration;

        public bool IsDone => _remainingAcks.Count == 0;

        public HandleResult Handle(string commandName, Bytes data)
        {
            if (_remainingAcks.Count == 0 || !string.Equals(Command, commandName, StringComparison.OrdinalIgnoreCase))
                return HandleResult.Unhandled;

            if (!_remainingAcks.TryGetValue(data, out var remaining))
                return HandleResult.Unhandled;

            if (remaining == 1)
                _remainingAcks.Remove(data);
            else
                _remainingAcks[data] = remaining - 1;

            var result = _remainingAcks.Count == 0
                ? HandleResult.Completed
                : HandleResult.Handled;

            if (result == HandleResult.Completed && ct.TryComplete())
                taskCompletionSource.SetResult(new RespSubscriptionAck(Acks));

            return result;
        }

        public void CompleteWithException(Exception exception)
        {
            if (ct.TryComplete())
                taskCompletionSource.SetException(exception);
        }

        private static Dictionary<Bytes, int> CountAcknowledgements(Bytes[] expected)
        {
            var counts = new Dictionary<Bytes, int>(expected.Length);
            foreach (var acknowledgement in expected)
                CollectionsMarshal.GetValueRefOrAddDefault(counts, acknowledgement, out _)++;
            return counts;
        }
    }
}
