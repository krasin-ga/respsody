using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespSet(RespAggregate respAggregate, DisposalGuard guard) : IRespResponse
{
    public int Length { get; } = respAggregate.Length - 1;
    public OwnedRespValueVariant this[int i]
    {
        get
        {
            guard.CheckDisposed();
            return new OwnedRespValueVariant(respAggregate[i + 1], guard);
        }
    }

    public T[] ToArrayOf<T>(IRespCodec codec)
    {
        guard.CheckDisposed();

        var array = new T[Length];
        for (var i = 0; i < Length; i++)
            array[i] = codec.Decode<T>(new OwnedRespValueVariant(respAggregate[i + 1], guard));

        return array;
    }

    public T[] ToArrayOf<T>(Decode<T> decode)
    {
        guard.CheckDisposed();

        var array = new T[Length];
        for (var i = 0; i < Length; i++)
            array[i] = decode(new OwnedRespValueVariant(respAggregate[i + 1], guard));
        return array;
    }

    public HashSet<T> ToHashSetOf<T>(IRespCodec codec)
    {
        guard.CheckDisposed();

        var hashSet = new HashSet<T>(Length);
        for (var i = 0; i < Length; i++)
            hashSet.Add(codec.Decode<T>(new OwnedRespValueVariant(respAggregate[i + 1], guard)));

        return hashSet;
    }

    public HashSet<T> ToHashSetOf<T>(Decode<T> decode)
    {
        guard.CheckDisposed();

        var hashSet = new HashSet<T>(Length);
        for (var i = 0; i < Length; i++)
            hashSet.Add(decode(new OwnedRespValueVariant(respAggregate[i + 1], guard)));

        return hashSet;
    }

    public void Dispose()
    {
        if(guard.TryDispose())
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
