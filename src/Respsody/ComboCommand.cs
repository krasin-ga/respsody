using Respsody.Client;
using Respsody.Network;
using Respsody.Resp;

namespace Respsody;

public class ComboCommand<T>
    where T : IRespResponse
{
    private readonly ValueTask<T> _futureCompletion;

    internal TransmitUnit<RespClient.Payload> TransmitUnit { get; private set; }

    internal ComboCommand(Command<T> command, RespClient.Payload payload, ValueTask<T> futureCompletion)
    {
        _futureCompletion = futureCompletion;
        TransmitUnit = new TransmitUnit<RespClient.Payload>(command.OutgoingBuffer, payload);
    }

    public async ValueTask<(T Result, IDisposable Lifetime)> AsOwnedFuture()
    {
        var result = await _futureCompletion;
        return result.AsExternallyOwnedUnsafe<T>();
    }

    public ValueTask<T> Future()
    {
        return _futureCompletion;
    }

    internal async ValueTask<Outcome> Observe()
    {
        try
        {
            var result = await _futureCompletion;
            return new Outcome(result.AsExternallyOwnedUnsafe<T>(), null);
        }
        catch (Exception exception)
        {
            return new Outcome(default, exception);
        }
    }

    internal void Discard()
    {
        try
        {
            if (_futureCompletion.IsCompleted)
                _futureCompletion.GetAwaiter().GetResult().Dispose();
        }
        catch (Exception)
        {
        }
    }

    internal readonly struct Outcome((T Result, IDisposable Lifetime) result, Exception? error) : IDisposable
    {
        public (T Result, IDisposable Lifetime) OwnedResult { get; } = result;
        public Exception? Error { get; } = error;
        public void Dispose() => OwnedResult.Lifetime?.Dispose();
    }
}
