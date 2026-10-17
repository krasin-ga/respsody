using Respsody.Client;
using Respsody.Library.Disposables;
using System.Runtime.ExceptionServices;
using Respsody.Network;
using Respsody.Resp;

namespace Respsody;

public readonly struct Combo(IDirectRespClient client, int timeout = int.MaxValue, CancellationToken token = default) : IDisposable
{
    internal readonly List<TransmitUnit<RespClient.Payload>> PreparedPayload = [];
    private readonly CommandOwnership _ownedCommands = [];

    internal void AddPayload(TransmitUnit<RespClient.Payload> payload)
    {
        PreparedPayload.Add(payload);
    }

    public ComboCommand<TResult> CreateCommand<TResult, TArg>(TArg arg, Func<IRespClient, TArg, Command<TResult>> commandFactory)
        where TResult : IRespResponse
    {
        CheckAvailable();
        Command<TResult>? command = null;
        try
        {
            command = commandFactory(client, arg);
            command.WithTimeout(timeout);
            var packed = client.Pack(this, command, token);
            _ownedCommands.Add(command);
            return packed;
        }
        catch (Exception exception)
        {
            command?.Dispose();
            Reject(exception);
            Dispose();
            throw;
        }
    }

    public ComboCommand<TResult> CreateCommand<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
        where TResult : IRespResponse
    {
        CheckAvailable();
        Command<TResult>? command = null;
        try
        {
            command = commandFactory(client);
            command.WithTimeout(timeout);
            var packed = client.Pack(this, command, token);
            _ownedCommands.Add(command);
            return packed;
        }
        catch (Exception exception)
        {
            command?.Dispose();
            Reject(exception);
            Dispose();
            throw;
        }
    }

    public void Execute()
    {
        CheckAvailable();
        _ownedCommands.ExecutionStarted = true;
        try
        {
            client.Execute(this);
            _ownedCommands.Submitted = true;
        }
        catch (Exception exception)
        {
            Reject(exception);
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_ownedCommands.Disposed)
            return;

        _ownedCommands.Disposed = true;
        if (!_ownedCommands.Submitted)
            Reject(new ObjectDisposedException(nameof(Combo)));

        foreach (var ownedCommand in _ownedCommands)
            ownedCommand.Dispose();
        _ownedCommands.Clear();
    }

    private void Reject(Exception exception)
    {
        foreach (var unit in PreparedPayload)
        {
            unit.Payload.CompleteWithException(exception, Environment.TickCount);
            unit.Page.FreeByDeliveryPipeline();
        }
        PreparedPayload.Clear();
    }

    private void CheckAvailable()
    {
        if (_ownedCommands.Disposed)
            throw new ObjectDisposedException(nameof(Combo));
        if (_ownedCommands.ExecutionStarted)
            throw new InvalidOperationException("The Combo has already been submitted.");
    }

    private Exception? ExecuteAndDispose()
    {
        CheckAvailable();
        try
        {
            Execute();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
        finally
        {
            Dispose();
        }
    }

    private sealed class CommandOwnership : List<IDisposable>
    {
        public bool Submitted { get; set; }
        public bool ExecutionStarted { get; set; }
        public bool Disposed { get; set; }
    }

    public static Builder0 Build(IDirectRespClient client, int timeout = int.MaxValue, CancellationToken token = default)
    {
        var combo = new Combo(client, timeout, token);
        return new Builder0(combo);
    }

    public class Builder0(Combo parent)
    {
        public Builder1<TResult> Cmd<TArg, TResult>(TArg arg, Func<IRespClient, TArg, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            return new Builder1<TResult>(parent, parent.CreateCommand(arg, commandFactory));
        }

        public Builder1<TResult> Cmd<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            return new Builder1<TResult>(parent, parent.CreateCommand(commandFactory));
        }
    }

    public class Builder1<T1>(Combo parent, ComboCommand<T1> c1)
        where T1 : IRespResponse
    {
        public Builder2<T1, TResult> Cmd<TArg, TResult>(TArg arg, Func<IRespClient, TArg, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            try
            {
                return new Builder2<T1, TResult>(parent, c1, parent.CreateCommand(arg, commandFactory));
            }
            catch
            {
                if (!parent._ownedCommands.ExecutionStarted)
                    c1.Discard();
                throw;
            }
        }

        public Builder2<T1, TResult> Cmd<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            try
            {
                return new Builder2<T1, TResult>(parent, c1, parent.CreateCommand(commandFactory));
            }
            catch
            {
                if (!parent._ownedCommands.ExecutionStarted)
                    c1.Discard();
                throw;
            }
        }

        public ValueTask<T1> Execute()
        {
            parent.ExecuteAndDispose();
            return c1.Future();
        }
    }

    public class Builder2<T1, T2>(Combo parent, ComboCommand<T1> c1, ComboCommand<T2> c2)
        where T1 : IRespResponse
        where T2 : IRespResponse
    {
        public Builder3<T1, T2, TResult> Cmd<TArg, TResult>(TArg arg, Func<IRespClient, TArg, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            try
            {
                return new Builder3<T1, T2, TResult>(parent, c1, c2, parent.CreateCommand(arg, commandFactory));
            }
            catch
            {
                if (!parent._ownedCommands.ExecutionStarted)
                {
                    c1.Discard();
                    c2.Discard();
                }
                throw;
            }
        }

        public Builder3<T1, T2, TResult> Cmd<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            try
            {
                return new Builder3<T1, T2, TResult>(parent, c1, c2, parent.CreateCommand(commandFactory));
            }
            catch
            {
                if (!parent._ownedCommands.ExecutionStarted)
                {
                    c1.Discard();
                    c2.Discard();
                }
                throw;
            }
        }

        public async ValueTask<DisposableTuple<T1, T2>> Execute()
        {
            var failure = parent.ExecuteAndDispose();
            var r1 = await c1.Observe();
            var r2 = await c2.Observe();
            if ((failure ?? r1.Error ?? r2.Error) is { } error)
            {
                r1.Dispose();
                r2.Dispose();
                ExceptionDispatchInfo.Throw(error);
            }
            return new DisposableTuple<T1, T2>(r1.OwnedResult, r2.OwnedResult);
        }
    }

    public class Builder3<T1, T2, T3>(Combo parent, ComboCommand<T1> c1, ComboCommand<T2> c2, ComboCommand<T3> c3)
        where T1 : IRespResponse
        where T2 : IRespResponse
        where T3 : IRespResponse
    {
        public Builder4<T1, T2, T3, TResult> Cmd<TArg, TResult>(TArg arg, Func<IRespClient, TArg, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            try
            {
                return new Builder4<T1, T2, T3, TResult>(parent, c1, c2, c3, parent.CreateCommand(arg, commandFactory));
            }
            catch
            {
                if (!parent._ownedCommands.ExecutionStarted)
                {
                    c1.Discard();
                    c2.Discard();
                    c3.Discard();
                }
                throw;
            }
        }

        public Builder4<T1, T2, T3, TResult> Cmd<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            try
            {
                return new Builder4<T1, T2, T3, TResult>(parent, c1, c2, c3, parent.CreateCommand(commandFactory));
            }
            catch
            {
                if (!parent._ownedCommands.ExecutionStarted)
                {
                    c1.Discard();
                    c2.Discard();
                    c3.Discard();
                }
                throw;
            }
        }

        public async ValueTask<DisposableTuple<T1, T2, T3>> Execute()
        {
            var failure = parent.ExecuteAndDispose();
            var r1 = await c1.Observe();
            var r2 = await c2.Observe();
            var r3 = await c3.Observe();
            if ((failure ?? r1.Error ?? r2.Error ?? r3.Error) is { } error)
            {
                r1.Dispose();
                r2.Dispose();
                r3.Dispose();
                ExceptionDispatchInfo.Throw(error);
            }
            return new DisposableTuple<T1, T2, T3>(r1.OwnedResult, r2.OwnedResult, r3.OwnedResult);
        }
    }

    public class Builder4<T1, T2, T3, T4>(
        Combo parent,
        ComboCommand<T1> c1,
        ComboCommand<T2> c2,
        ComboCommand<T3> c3,
        ComboCommand<T4> c4)
        where T1 : IRespResponse
        where T2 : IRespResponse
        where T3 : IRespResponse
        where T4 : IRespResponse
    {
        public Builder5<T1, T2, T3, T4, TResult> Cmd<TArg, TResult>(TArg arg, Func<IRespClient, TArg, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            try
            {
                return new Builder5<T1, T2, T3, T4, TResult>(parent, c1, c2, c3, c4, parent.CreateCommand(arg, commandFactory));
            }
            catch
            {
                if (!parent._ownedCommands.ExecutionStarted)
                {
                    c1.Discard();
                    c2.Discard();
                    c3.Discard();
                    c4.Discard();
                }
                throw;
            }
        }

        public Builder5<T1, T2, T3, T4, TResult> Cmd<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            try
            {
                return new Builder5<T1, T2, T3, T4, TResult>(parent, c1, c2, c3, c4, parent.CreateCommand(commandFactory));
            }
            catch
            {
                if (!parent._ownedCommands.ExecutionStarted)
                {
                    c1.Discard();
                    c2.Discard();
                    c3.Discard();
                    c4.Discard();
                }
                throw;
            }
        }

        public async ValueTask<DisposableTuple<T1, T2, T3, T4>> Execute()
        {
            var failure = parent.ExecuteAndDispose();
            var r1 = await c1.Observe();
            var r2 = await c2.Observe();
            var r3 = await c3.Observe();
            var r4 = await c4.Observe();
            if ((failure ?? r1.Error ?? r2.Error ?? r3.Error ?? r4.Error) is { } error)
            {
                r1.Dispose();
                r2.Dispose();
                r3.Dispose();
                r4.Dispose();
                ExceptionDispatchInfo.Throw(error);
            }
            return new DisposableTuple<T1, T2, T3, T4>(r1.OwnedResult, r2.OwnedResult, r3.OwnedResult, r4.OwnedResult);
        }
    }

    public class Builder5<T1, T2, T3, T4, T5>(
        Combo parent,
        ComboCommand<T1> c1,
        ComboCommand<T2> c2,
        ComboCommand<T3> c3,
        ComboCommand<T4> c4,
        ComboCommand<T5> c5)
        where T1 : IRespResponse
        where T2 : IRespResponse
        where T3 : IRespResponse
        where T4 : IRespResponse
        where T5 : IRespResponse
    {
        public async ValueTask<DisposableTuple<T1, T2, T3, T4, T5>> Execute()
        {
            var failure = parent.ExecuteAndDispose();
            var r1 = await c1.Observe();
            var r2 = await c2.Observe();
            var r3 = await c3.Observe();
            var r4 = await c4.Observe();
            var r5 = await c5.Observe();
            if ((failure ?? r1.Error ?? r2.Error ?? r3.Error ?? r4.Error ?? r5.Error) is { } error)
            {
                r1.Dispose();
                r2.Dispose();
                r3.Dispose();
                r4.Dispose();
                r5.Dispose();
                ExceptionDispatchInfo.Throw(error);
            }
            return new DisposableTuple<T1, T2, T3, T4, T5>(
                r1.OwnedResult, r2.OwnedResult, r3.OwnedResult, r4.OwnedResult, r5.OwnedResult);
        }
    }
}
