using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    private readonly SemaphoreSlim _initialConnectionLock = new(1);
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
    public ConnectionMetadata? Metadata { get; private set; }
    public IReadOnlyDictionary<string, string?> ConnectionConfig => connectionProcedure.Config;

    internal async Task Connect()
    {
        using var cts = new CancellationTokenSource();
        if (connectionProcedure.Timeout > TimeSpan.Zero)
            cts.CancelAfter(connectionProcedure.Timeout);

        await _initialConnectionLock.WaitAsync(cts.Token);

        if (_structuredSocket is { })
        {
            _initialConnectionLock.Release();
            return;
        }

        try
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

            await _structuredSocket.Connected.WaitAsync(cts.Token);

            _ = Task.Run(CheckExpiredMessages, CancellationToken.None);
        }
        finally
        {
            _initialConnectionLock.Release();
        }
    }

    private async Task CheckExpiredMessages()
    {
        if (options.TimeoutCheckInterval <= TimeSpan.Zero)
            return;

        var timer = new PeriodicTimer(options.TimeoutCheckInterval);

        while (await timer.WaitForNextTickAsync())
        {
            if (_structuredSocket?.IsDisposed is true)
                break;

            var ticks = Environment.TickCount;
            foreach (var responseData in _responseQueue)
                responseData.TryExpire(ticks);
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

        if (_structuredSocket.IsDisposed)
        {
            command.Dispose();
            throw new RespClientDisposedException();
        }

        var taskCompletionSource = PooledValueTaskCompletionSource<T>.Create(
            _runContinuationsAsynchronously
        );

        Bytes[]? subAcks = null;
        var responseType = ResponseTypeMapping.GetResponseTypeEnum<T>();
        if (responseType == ResponseType.Subscription)
        {
            subAcks = CommandParser.ParseArgumentsSlow(command);
            cancellationToken = CancellationToken.None;
            command.WithTimeout(int.MaxValue);
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

        return taskCompletionSource.AsValueTask();
    }

    public ComboCommand<T> Pack<T>(Combo combo, Command<T> command, CancellationToken token = default) where T : IRespResponse
    {
        if (_structuredSocket?.IsDisposed is true)
        {
            command.Dispose();
            throw new RespClientDisposedException();
        }

        command.FinalizeCommand();

        var responseCompletionSource = PooledValueTaskCompletionSource<T>.Create(
            _runContinuationsAsynchronously
        );

        var responseType = ResponseTypeMapping.GetResponseTypeEnum<T>();
        if (responseType == ResponseType.Subscription)
            throw new InvalidOperationException("Cannot pack subscription");

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
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (command.Timeout > 0)
            linkedCts.CancelAfter(command.Timeout);

        await Connect().WaitAsync(linkedCts.Token);
        return await ExecuteCommand(command, linkedCts.Token);
    }

    public void HandleIncoming(ReadyFrames<RespContext> readyFrames)
    {
        var ticks = Environment.TickCount;

        using (readyFrames)
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

    private void HandleAggregate(RespAggregate aggregate, int ticks)
    {
        if (aggregate.Context.Type == RespType.Attribute)
        {
            _attribute = aggregate;
            return;
        }

        if (aggregate.HeaderFrame.Context.Type == RespType.Push)
        {
            var respPush = new RespPush(aggregate);
            if (respPush.TryGetSubscription(out var data))
            {
                var idx = 0;
                foreach (var confirmation in _subUnSubConfirmationsQueue)
                {
                    if (confirmation.Handle(data.Command, data.Ack) is var res && res != SubUnSub.HandleResult.Unhandled)
                    {
                        if (_responseQueue.TryPeek(out var peeked) && peeked.IsSubscription(confirmation.Command, confirmation.Acks))
                            _responseQueue.TryDequeue(out _);

                        if (idx == 0 && res == SubUnSub.HandleResult.Completed)
                            _subUnSubConfirmationsQueue.TryDequeue(out _);
                        break;
                    }

                    idx++;
                }

                return;
            }

            _pushReceiver.Receive(respPush);
            return;
        }

        if (!_responseQueue.TryDequeue(out var payload))
            Panic("[handle aggregate] failed to dequeue response for completion");

        var attribute = _attribute;
        _attribute = null;

        payload.Complete(new RespResponse(Frame: null, aggregate, attribute), ticks);
    }

    private void HandleSimpleResponse(Frame<RespContext> frame, int ticks)
    {
        if (!_responseQueue.TryDequeue(out var requestData))
            Panic("[handle simple response] failed to dequeue response for completion");

        var attribute = _attribute;
        _attribute = null;

        if (frame.Context.Type is RespType.BulkError or RespType.SimpleError)
        {
            using var errorString = new RespString(frame);
            var exception = new RespErrorResponseException(errorString.ToString(Encoding.UTF8) ?? "NULL");
            requestData.CompleteWithException(exception, ticks);
            return;
        }

        requestData.Complete(new RespResponse(Frame: frame, null, attribute), ticks);
    }

    public void OnDisconnected(Exception? exception, int generation)
    {
        _clientHandler?.OnDisconnected(this, exception, generation);

        _respFrameAggregationStrategy.Reset();

        while (_responseQueue.TryPeek(out var response) && response.ConnectionGeneration <= generation)
        {
            if (!_responseQueue.TryDequeue(out _))
                Panic("[on disconnected] failed to dequeue response for completion with error");

            response.CompleteWithException(exception ?? new RespConnectionLostException(), ticks: Environment.TickCount);
        }

        while (_subUnSubConfirmationsQueue.TryPeek(out var sub) && sub.ConnectionGeneration <= generation)
        {
            if (!_subUnSubConfirmationsQueue.TryDequeue(out _))
                Panic("[on disconnected] failed to dequeue sub for completion with error");

            sub.CompleteWithException(exception ?? new RespConnectionLostException());
        }
    }

    public async ValueTask InitializeConnection(ConnectedSocket connectedSocket, int generation, CancellationToken cancellationToken)
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
        _initialConnectionLock.Dispose();
        _attribute?.Dispose();
        _structuredSocket?.Dispose();
        _handlersHandle?.Free();
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
        private readonly CompletionToken _ct = new();

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

                respClient._clientHandler?.OnCommandTimedOut(respClient, elapsed, command);

                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                InternalCompleteWithException(
                    new OperationCanceledException("Command was cancelled before being sent to the server"));

                respClient._clientHandler?.OnCommandCancelled(respClient, elapsed, command);

                return false;
            }

            //mutate before copying 
            ConnectionGeneration = socketId;
            respClient._responseQueue.Enqueue(this);
            if (responseType == ResponseType.Subscription)
                respClient._subUnSubConfirmationsQueue.Enqueue(new SubUnSub(command, subAcks!, socketId, taskCompletionSource, _ct));

            return true;
        }

        public bool TryExpire(int ticks)
        {
            var elapsed = ticks - startTicks;
            if (elapsed > timeout)
            {
                InternalCompleteWithException(
                    new TimeoutException($"The command expired before receiving a response. Timeout={timeout}ms, Elapsed={elapsed}ms")
                );

                respClient._clientHandler?.OnCommandTimedOut(respClient, elapsed, command);

                return true;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                InternalCompleteWithException(
                    new OperationCanceledException("The command was canceled before receiving a response")
                );

                respClient._clientHandler?.OnCommandCancelled(respClient, elapsed, command);

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

            respClient._clientHandler?.OnCommandCancelled(respClient, ticks - startTicks, command);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CompleteWithException(Exception exception, int ticks)
        {
            if (_ct?.CanComplete() is false)
                return;

            taskCompletionSource.SetException(exception);
            respClient._clientHandler?.OnCommandFailed(respClient, ticks - startTicks, command);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalCompleteWithException(Exception exception)
        {
            if (_ct?.CanComplete() is false)
                return;

            taskCompletionSource.SetException(exception);
        }

        public void Complete(RespResponse response, int ticks)
        {
            if (_ct?.CanComplete() is false)
                return;

            respClient._clientHandler?.OnCommandExecuted(respClient, ticks - startTicks, command);

            switch (responseType)
            {
                case ResponseType.String:
                    CompleteStringResponse(response);
                    break;
                case ResponseType.Boolean:
                    CompleteBooleanResponse(response);
                    break;
                case ResponseType.Double:
                    CompleteDoubleResponse(response);
                    break;
                case ResponseType.Number:
                    CompleteNumberResponse(response);
                    break;
                case ResponseType.BigNumber:
                    CompleteBigNumberResponse(response);
                    break;
                case ResponseType.Array:
                    CompleteArrayResponse(response);
                    break;
                case ResponseType.Map:
                    CompleteMapResponse(response);
                    break;
                case ResponseType.Set:
                    CompleteSetResponse(response);
                    break;
                case ResponseType.Untyped:
                    taskCompletionSource.SetResult(response);
                    break;
                case ResponseType.Void:
                    taskCompletionSource.SetResult(new RespVoid());
                    response.Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteSetResponse(RespResponse response)
        {
            if (response.Aggregate is not { } aggregate || !RespSet.CanConvert(aggregate.HeaderFrame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Set, response));
                return;
            }

            taskCompletionSource.SetResult(new RespSet(aggregate));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteArrayResponse(RespResponse response)
        {
            if (response.Aggregate is not { } aggregate || !RespArray.CanConvert(aggregate.HeaderFrame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Array, response));
                return;
            }

            taskCompletionSource.SetResult(new RespArray(aggregate));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteMapResponse(RespResponse response)
        {
            if (response.Aggregate is not { } aggregate || !RespMap.CanConvert(aggregate.HeaderFrame))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Map, response));
                return;
            }

            taskCompletionSource.SetResult(new RespMap(aggregate));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteStringResponse(RespResponse response)
        {
            if (response.Frame is not { } sliceMemory || !RespString.CanConvert(sliceMemory))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.String, response));
                return;
            }

            taskCompletionSource.SetResult(new RespString(sliceMemory));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteNumberResponse(RespResponse response)
        {
            if (response.Frame is not { } sliceMemory || !RespNumber.CanConvert(sliceMemory))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Number, response));
                return;
            }

            taskCompletionSource.SetResult(new RespNumber(sliceMemory));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteBigNumberResponse(RespResponse response)
        {
            if (response.Frame is not { } sliceMemory || !RespBigNumber.CanConvert(sliceMemory))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.BigNumber, response));
                return;
            }

            taskCompletionSource.SetResult(new RespBigNumber(sliceMemory));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteDoubleResponse(RespResponse response)
        {
            if (response.Frame is not { } sliceMemory || !RespDouble.CanConvert(sliceMemory))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Double, response));
                return;
            }

            taskCompletionSource.SetResult(new RespDouble(sliceMemory));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteBooleanResponse(RespResponse response)
        {
            if (response.Frame is not { } sliceMemory || !RespBoolean.CanConvert(sliceMemory))
            {
                taskCompletionSource.SetException(new RespUnexpectedResponseException(ResponseType.Boolean, response));
                return;
            }

            taskCompletionSource.SetResult(new RespBoolean(sliceMemory));
        }

        internal bool IsSubscription(string cmd, Bytes[] acks)
        {
            return responseType is ResponseType.Subscription
                   && string.Equals(cmd, command, StringComparison.OrdinalIgnoreCase)
                   && acks.SequenceEqual(subAcks!);
        }
    }

    private class SubUnSub(
        string command,
        Bytes[] acks,
        int connectionGeneration,
        ITaskCompletionSource taskCompletionSource,
        CompletionToken ct)
    {
        public enum HandleResult
        {
            Unhandled,
            Handled,
            Completed
        }

        private readonly HashSet<Bytes> _remainingAcks = [.. acks];
        public string Command => command;
        public Bytes[] Acks => acks;
        public int ConnectionGeneration => connectionGeneration;

        public bool IsDone => _remainingAcks.Count == 0;

        public HandleResult Handle(string commandName, Bytes data)
        {
            if (_remainingAcks.Count == 0 || !string.Equals(Command, commandName, StringComparison.OrdinalIgnoreCase))
                return HandleResult.Unhandled;

            if (!_remainingAcks.Remove(data))
                return HandleResult.Unhandled;

            var result = _remainingAcks.Count == 0
                ? HandleResult.Completed
                : HandleResult.Handled;

            if (result == HandleResult.Completed && ct.CanComplete())
                taskCompletionSource.SetResult(new RespSubscriptionAck(Acks));

            return result;
        }

        public void CompleteWithException(Exception exception)
        {
            if (ct.CanComplete())
                taskCompletionSource.SetException(exception);
        }
    }

    private class CompletionToken
    {
        private int _isCompleted;

        public bool CanComplete()
        {
            return Interlocked.CompareExchange(ref _isCompleted, 1, 0) == 0;
        }
    }
}