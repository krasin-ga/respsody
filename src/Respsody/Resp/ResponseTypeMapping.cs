using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace Respsody.Resp;

public static class ResponseTypeMapping
{
    private static readonly FrozenDictionary<Type, ResponseType> Mapping = new Dictionary<Type, ResponseType>
    {
        { typeof(RespArray), ResponseType.Array },
        { typeof(RespBigNumber), ResponseType.BigNumber },
        { typeof(RespBoolean), ResponseType.Boolean },
        { typeof(RespDouble), ResponseType.Double },
        { typeof(RespString), ResponseType.String },
        { typeof(RespSet), ResponseType.Set },
        { typeof(RespNumber), ResponseType.Number },
        { typeof(RespVoid), ResponseType.Void },
        { typeof(RespMap), ResponseType.Map },
        { typeof(RespSubscriptionAck), ResponseType.Subscription },
        { typeof(RespResponse), ResponseType.Untyped }
    }.ToFrozenDictionary();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ResponseType GetResponseTypeEnum<T>()
    {
        if (Mapping.TryGetValue(typeof(T), out var type))
            return type;

        throw new ArgumentOutOfRangeException();
    }
}