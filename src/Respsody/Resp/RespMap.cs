using System.Runtime.CompilerServices;
using Respsody.Exceptions;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespMap(RespAggregate respAggregate) : IDisposable, IRespResponse
{
    public int Length { get; } = (respAggregate.Length - 1) / 2;

    public static async ValueTask<RespMap> FromResponseTask(
        ValueTask<RespResponse> responseTask)
    {
        var response = await responseTask;
        try
        {
            if (response.Aggregate is not { } slicedRespAggregate)
                throw new RespUnexpectedResponseException(ResponseType.Map, response);

            return slicedRespAggregate.ToRespMap();
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public IReadOnlyDictionary<TKey, object?> ToMapOf<TKey>(DecodeSlice<TKey> decode)
        where TKey : notnull
    {
        var length = respAggregate.Length;
        var dictionary = new Dictionary<TKey, object?>(length);
        for (var i = 1; i < length; i += 2)
        {
            var key = respAggregate[i];
            if (key.Simple is null)
                throw new InvalidOperationException("Expected key not to be a collection");

            dictionary[decode(key.Simple.Value)] = respAggregate[i + 1].ToClrValue();
        }

        return dictionary;
    }

    public IReadOnlyDictionary<string, object?> ToMapWithStringKey()
    {
        return ToMapOf(
            static (in Frame<RespContext> slice)
                => slice.ToRespString().ToString()!);
    }

    public void Dispose()
    {
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