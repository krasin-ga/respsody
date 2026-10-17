using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespPush : IDisposable
{
    private readonly RespAggregate _respAggregate;
    private readonly DisposalGuard _guard;

    internal RespPush(RespAggregate respAggregate, DisposalGuard guard)
    {
        _respAggregate = respAggregate;
        _guard = guard;
        Length = respAggregate.Length - 1;
    }

    public int Length { get; }

    public OwnedRespValueVariant this[int i]
    {
        get
        {
            _guard.CheckDisposed();
            return new OwnedRespValueVariant(_respAggregate[i + 1], _guard);
        }
    }

    public T[] ToArrayOf<T>(IRespCodec codec)
    {
        _guard.CheckDisposed();

        var array = new T[Length];
        for (var i = 0; i < Length; i++)
            array[i] = codec.Decode<T>(new OwnedRespValueVariant(_respAggregate[i + 1], _guard));

        return array;
    }

    public T[] ToArrayOf<T>(Decode<T> decode)
    {
        _guard.CheckDisposed();

        var array = new T[Length];
        for (var i = 0; i < Length; i++)
            array[i] = decode(new OwnedRespValueVariant(_respAggregate[i + 1], _guard));

        return array;
    }

    public void Dispose()
    {
        if(_guard.TryDispose())
            _respAggregate.Dispose();
    }

    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Push;
    }
}
