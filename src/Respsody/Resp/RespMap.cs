using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespMap(RespAggregate respAggregate, DisposalGuard guard) : IRespResponse
{
    public int Length { get; } = (respAggregate.Length - 1) / 2;

    public IReadOnlyDictionary<TKey, object?> ToMapOf<TKey>(DecodeFrame<TKey> decode)
        where TKey : notnull
    {
        var length = respAggregate.Length;
        var dictionary = new Dictionary<TKey, object?>(length);
        for (var i = 1; i < length; i += 2)
        {
            var key = respAggregate[i];
            if (key.Simple is null)
                throw new InvalidOperationException("Expected key not to be a collection");

            dictionary[decode(new OwnedRespFrame(key.Simple.Value, guard))]
                = new OwnedRespValueVariant(respAggregate[i + 1], guard).ToClrValue();
        }

        return dictionary;
    }

    public IReadOnlyDictionary<string, object?> ToMapWithStringKey()
    {
        return ToMapOf(static (in OwnedRespFrame slice)
            => slice.ToRespStringView().ToString()!);
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
        return (Unsafe.As<RespMap, T>(ref @this), respAggregate.AsExternallyOwned());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Map;
    }
}