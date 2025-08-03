using System.Runtime.CompilerServices;
using Respsody.Exceptions;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespSet(RespAggregate respAggregate) : IDisposable, IRespResponse
{
    public int Length { get; } = respAggregate.Length - 1;
    public RespValueVariant this[int i] => respAggregate[i + 1];

    public static async ValueTask<RespSet> FromResponseTask(
        ValueTask<RespResponse> responseTask)
    {
        var response = await responseTask;
        try
        {
            if (response.Aggregate is not { } slicedRespAggregate)
                throw new RespUnexpectedResponseException(ResponseType.Set, response);

            return slicedRespAggregate.ToRespSet();
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

    public HashSet<T> ToHashSetOf<T>(IRespCodec codec)
    {
        var hashSet = new HashSet<T>(Length);
        for (var i = 0; i < Length; i++)
            hashSet.Add(codec.Decode<T>(this[i]));

        return hashSet;
    }

    public HashSet<T> ToHashSetOf<T>(Decode<T> decode)
    {
        var hashSet = new HashSet<T>(Length);
        for (var i = 0; i < Length; i++)
            hashSet.Add(decode(this[i]));

        return hashSet;
    }

    public void Dispose()
    {
        respAggregate.Dispose();
    }

    public (T, IDisposable) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse
    {
        var @this = this;
        return (Unsafe.As<RespSet, T>(ref @this), respAggregate.AsExternallyOwned());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Set;
    }
}