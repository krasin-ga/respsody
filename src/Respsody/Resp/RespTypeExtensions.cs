namespace Respsody.Resp;

public static class RespTypeExtensions
{
    public static bool IsCollection(this RespType respType)
    {
        return respType is RespType.Array or RespType.Map
            or RespType.Set or RespType.Attribute or RespType.Push;
    }

    public static bool IsBulk(this RespType respType)
    {
        return respType is RespType.BulkError or RespType.BulkString;
    }

    public static bool IsLengthPrefixed(this RespType respType)
    {
        return respType is RespType.Array or RespType.Map
            or RespType.Set or RespType.BulkString
            or RespType.VerbatimString or RespType.BulkError
            or RespType.SteamedStringChunk or RespType.Push or RespType.Attribute;
    }
}