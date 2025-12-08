using System.Collections;
using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespArray(RespAggregate respAggregate, DisposalGuard guard) : IRespResponse
{
    public int Length { get; } = respAggregate.Length - 1;

    public OwnedRespValueVariant this[int i] => new(respAggregate[i + 1], guard);

    public Enumerator<RespString> EnumerateStrings()
    {
        return new Enumerator<RespString>(respAggregate, variant => variant.ToRespStringView(), guard);
    }

    public Enumerator<T> Enumerate<T>(Func<OwnedRespValueVariant, T> convert)
        where T : IRespResponse
    {
        return new Enumerator<T>(respAggregate, convert, guard);
    }

    public T[] ToArrayOf<T>(IRespCodec codec)
    {
        guard.CheckDisposed();

        var array = new T[Length];
        for (var i = 0; i < Length; i++)
            array[i] = codec.Decode<T>(this[i]);

        return array;
    }

    public T[] ToArrayOf<T>(Decode<T> decode)
    {
        guard.CheckDisposed();

        var array = new T[Length];
        for (var i = 0; i < Length; i++)
            array[i] = decode(this[i]);

        return array;
    }

    public void Dispose()
    {
        if (guard.TryDispose())
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

    public struct Enumerator<T>(RespAggregate data, Func<OwnedRespValueVariant, T> convert, DisposalGuard guard) : IEnumerator<T>
    {
        private int _index = -1;
        private T _current = default!;

        public bool MoveNext()
        {
            if (++_index >= data.Length)
                return false;

            _current = convert(new OwnedRespValueVariant(data[_index], guard));
            return true;
        }

        public readonly T Current => _current;

        readonly object IEnumerator.Current => Current!;

        public void Reset()
        {
            _index = -1;
            _current = default!;
        }

        public readonly void Dispose()
        {
        }
    }
}