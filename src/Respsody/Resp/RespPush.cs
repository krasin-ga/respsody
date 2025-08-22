using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespPush(RespAggregate respAggregate) : IDisposable
{
    public int Length { get; } = respAggregate.Length - 1;

    public RespValueVariant this[int i] =>
        respAggregate[i + 1];

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
        respAggregate.Dispose();
    }

    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Push;
    }
}