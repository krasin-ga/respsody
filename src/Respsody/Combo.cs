using Respsody.Client;
using Respsody.Library.Disposables;
using Respsody.Network;
using Respsody.Resp;

namespace Respsody;

public readonly struct Combo(IDirectRespClient client, int timeout = int.MaxValue, CancellationToken token = default) : IDisposable
{
    internal readonly List<TransmitUnit<RespClient.Payload>> PreparedPayload = [];
    private readonly List<IDisposable> _ownedCommands = [];

    internal void AddPayload(TransmitUnit<RespClient.Payload> payload)
    {
        PreparedPayload.Add(payload);
    }

    public ComboCommand<TResult> CreateCommand<TResult, TArg>(TArg arg, Func<IRespClient, TArg, Command<TResult>> commandFactory)
        where TResult : IRespResponse
    {
        var command = commandFactory(client, arg);
        command.WithTimeout(timeout);
        _ownedCommands.Add(command);

        return client.Pack(this, command, token);
    }

    public ComboCommand<TResult> CreateCommand<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
        where TResult : IRespResponse
    {
        var command = commandFactory(client);
        command.WithTimeout(timeout);
        _ownedCommands.Add(command);

        return client.Pack(this, command, token);
    }

    public void Execute()
    {
        client.Execute(this);
    }

    public void Dispose()
    {
        foreach (var ownedCommand in _ownedCommands)
            ownedCommand.Dispose();
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
            return new Builder2<T1, TResult>(parent, c1, parent.CreateCommand(arg, commandFactory));
        }

        public Builder2<T1, TResult> Cmd<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            return new Builder2<T1, TResult>(parent, c1, parent.CreateCommand(commandFactory));
        }

        public ValueTask<T1> Execute()
        {
            parent.Execute();
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
            return new Builder3<T1, T2, TResult>(parent, c1, c2, parent.CreateCommand(arg, commandFactory));
        }

        public Builder3<T1, T2, TResult> Cmd<TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            return new Builder3<T1, T2, TResult>(parent, c1, c2, parent.CreateCommand(commandFactory));
        }

        public async ValueTask<DisposableTuple<T1, T2>> Execute()
        {
            parent.Execute();
            return new DisposableTuple<T1, T2>(await c1.AsOwnedFuture(), await c2.AsOwnedFuture());
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
            return new Builder4<T1, T2, T3, TResult>(parent, c1, c2, c3, parent.CreateCommand(arg, commandFactory));
        }

        public Builder4<T1, T2, T3, TResult> Cmd<TArg, TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            return new Builder4<T1, T2, T3, TResult>(parent, c1, c2, c3, parent.CreateCommand(commandFactory));
        }

        public async ValueTask<DisposableTuple<T1, T2, T3>> Execute()
        {
            parent.Execute();
            return new DisposableTuple<T1, T2, T3>(await c1.AsOwnedFuture(), await c2.AsOwnedFuture(), await c3.AsOwnedFuture());
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
            parent.CreateCommand(arg, commandFactory);

            return new Builder5<T1, T2, T3, T4, TResult>(parent, c1, c2, c3, c4, parent.CreateCommand(arg, commandFactory));
        }

        public Builder5<T1, T2, T3, T4, TResult> Cmd<TArg, TResult>(Func<IRespClient, Command<TResult>> commandFactory)
            where TResult : IRespResponse
        {
            return new Builder5<T1, T2, T3, T4, TResult>(parent, c1, c2, c3, c4, parent.CreateCommand(commandFactory));
        }

        public async ValueTask<DisposableTuple<T1, T2, T3, T4>> Execute()
        {
            parent.Execute();
            return new DisposableTuple<T1, T2, T3, T4>(
                await c1.AsOwnedFuture(),
                await c2.AsOwnedFuture(),
                await c3.AsOwnedFuture(),
                await c4.AsOwnedFuture());
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
            parent.Execute();

            return new DisposableTuple<T1, T2, T3, T4, T5>(
                await c1.AsOwnedFuture(),
                await c2.AsOwnedFuture(),
                await c3.AsOwnedFuture(),
                await c4.AsOwnedFuture(),
                await c5.AsOwnedFuture());
        }
    }
}