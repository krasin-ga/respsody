using System.Runtime.CompilerServices;
using Respsody.Exceptions;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespArray(RespAggregate respAggregate) : IRespResponse
{
    public int Length { get; } = respAggregate.Length - 1;

    public RespValueVariant this[int i] =>
        respAggregate[i + 1];

    public static async ValueTask<RespArray> FromResponseTask(
        ValueTask<RespResponse> responseTask)
    {
        var response = await responseTask;
        try
        {
            if (response.Aggregate is not { } slicedRespAggregate)
                throw new RespUnexpectedResponseException(ResponseType.Array, response);

            return slicedRespAggregate.ToRespArray();
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

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

    public (T, IDisposable) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse
    {
        var @this = this;
        return (Unsafe.As<RespArray, T>(ref @this), respAggregate.AsExternallyOwned());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Array;
    }
}