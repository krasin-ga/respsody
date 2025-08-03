using System.Runtime.CompilerServices;
using Respsody.Memory;

namespace Respsody.Resp;

public static class RespTypesExtensions
{
    public static RespString ToRespString(this in RespValueVariant variant)
    {
        if (variant.Simple is not { } simple || !RespString.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespString(simple);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RespString ToRespString(this in Frame<RespContext> frame)
    {
        if (!RespString.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespString(frame);
    }

    public static RespDouble ToRespDouble(this in RespValueVariant variant)
    {
        if (variant.Simple is not { } simple || !RespDouble.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespDouble(simple);
    }

    public static RespDouble ToRespDouble(this in Frame<RespContext> frame)
    {
        if (!RespDouble.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespDouble(frame);
    }

    public static RespNumber ToRespNumber(this in RespValueVariant variant)
    {
        if (variant.Simple is not { } simple || !RespNumber.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespNumber(simple);
    }

    public static RespNumber ToRespNumber(this in Frame<RespContext> frame)
    {
        if (!RespNumber.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespNumber(frame);
    }

    public static RespBigNumber ToRespBigNumber(this in RespValueVariant variant)
    {
        if (variant.Simple is not { } simple || !RespBigNumber.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespBigNumber(simple);
    }

    public static RespBigNumber ToRespBigNumber(this in Frame<RespContext> frame)
    {
        if (!RespBigNumber.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespBigNumber(frame);
    }

    public static RespArray ToRespArray(this in RespValueVariant variant)
    {
        if (variant.Aggregate is not { } agg || !RespArray.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespArray(agg);
    }

    public static RespArray ToRespArray(this RespAggregate slice)
    {
        if (!RespArray.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespArray(slice);
    }

    public static RespMap ToRespMap(this in RespValueVariant variant)
    {
        if (variant.Aggregate is not { } agg || !RespMap.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespMap(agg);
    }

    public static RespMap ToRespMap(this RespAggregate slice)
    {
        if (!RespMap.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespMap(slice);
    }

    public static RespPush ToRespPush(this in RespValueVariant variant)
    {
        if (variant.Aggregate is not { } agg || !RespPush.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespPush(agg);
    }

    public static RespPush ToRespPush(this RespAggregate slice)
    {
        if (!RespPush.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespPush(slice);
    }

    public static RespSet ToRespSet(this in RespValueVariant variant)
    {
        if (variant.Aggregate is not { } agg || !RespSet.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespSet(agg);
    }

    public static RespSet ToRespSet(this RespAggregate slice)
    {
        if (!RespSet.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespSet(slice);
    }

    private static InvalidOperationException CreateConversionException(
        RespValueVariant variant,
        [CallerMemberName] string? caller = null)
    {
        return new InvalidOperationException(
            $"Cannot execute {caller} because variant is {variant.ToDebugString()}");
    }

    private static InvalidOperationException CreateConversionException(
        Frame<RespContext> frame,
        [CallerMemberName] string? caller = null)
    {
        return new InvalidOperationException(
            $"Cannot execute {caller} because variant is {frame.ToDebugString()}");
    }

    public static object? ToClrValue(this RespValueVariant valueVariant)
    {
        if (valueVariant.Simple.HasValue)
            return valueVariant.Simple.Value.ToClrValue();

        var aggregate = valueVariant.Aggregate;
        if (aggregate is null)
            return null;

        static object? Decode(in RespValueVariant v) =>
            v.ToClrValue();

        switch (valueVariant.Type)
        {
            case RespType.Array:
                return new RespArray(aggregate).ToArrayOf(Decode);
            case RespType.Map:
                return new RespMap(aggregate).ToMapWithStringKey();
            case RespType.Set:
                return new RespSet(aggregate).ToHashSetOf(Decode);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public static object? ToClrValue(this Frame<RespContext> memory)
    {
        return memory.Context.Type switch
        {
            RespType.None => null,
            RespType.Number => new RespNumber(memory).ToInt64(),
            RespType.Null => null,
            RespType.Double => new RespDouble(memory).ToDouble(),
            RespType.Boolean => new RespBoolean(memory).ToBool(),
            RespType.BulkError or RespType.SimpleError => new Exception(new RespString(memory).ToString()),
            RespType.VerbatimString or RespType.BulkString or RespType.SimpleString => new RespString(memory).ToString(),
            RespType.BigNumber => new RespBigNumber(memory).ToBigInteger(),
            RespType.Array or RespType.Map or RespType.Set or RespType.Attribute or RespType.Push or RespType.SteamedStringChunk or RespType.End
                => throw new ArgumentOutOfRangeException(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}