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

    public OwnedRespValueVariant this[int i] =>
        new (_respAggregate[i + 1], _guard);

    public T[] ToArrayOf<T>(IRespCodec codec)
    {
        var array = new T[Length];
        for (var i = 0; i < Length; i++)
            array[i] = codec.Decode<T>(this[i]);

        return array;
    }

    public T[] ToArrayOf<T>(Decode<T> decode)
    {
        var array = new T[Length];
        for (var i = 0; i < Length; i++)
            array[i] = decode(this[i]);

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