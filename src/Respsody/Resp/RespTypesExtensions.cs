using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public static class RespTypesExtensions
{
    public static RespString ToRespStringView(this in RespValueVariant variant)
    {
        if (variant.Simple is not { } simple || !RespString.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespString(simple, CompletionGuard.Restrictive);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RespString ToRespStringView(this in Frame<RespContext> frame)
    {
        if (!RespString.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespString(frame, CompletionGuard.Restrictive);
    }

    public static RespDouble ToRespDoubleView(this in RespValueVariant variant)
    {
        if (variant.Simple is not { } simple || !RespDouble.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespDouble(simple, CompletionGuard.Restrictive);
    }

    public static RespDouble ToRespDoubleView(this in Frame<RespContext> frame)
    {
        if (!RespDouble.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespDouble(frame, CompletionGuard.Restrictive);
    }

    public static RespNumber ToRespNumberView(this in RespValueVariant variant)
    {
        if (variant.Simple is not { } simple || !RespNumber.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespNumber(simple, CompletionGuard.Restrictive);
    }

    public static RespNumber ToRespNumberView(this in Frame<RespContext> frame)
    {
        if (!RespNumber.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespNumber(frame, CompletionGuard.Restrictive);
    }

    public static RespBigNumber ToRespBigNumberView(this in RespValueVariant variant)
    {
        if (variant.Simple is not { } simple || !RespBigNumber.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespBigNumber(simple, CompletionGuard.Restrictive);
    }

    public static RespBigNumber ToRespBigNumberView(this in Frame<RespContext> frame)
    {
        if (!RespBigNumber.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespBigNumber(frame, CompletionGuard.Restrictive);
    }

    public static RespArray ToRespArrayView(this in RespValueVariant variant)
    {
        if (variant.Aggregate is not { } agg || !RespArray.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespArray(agg, CompletionGuard.Restrictive);
    }

    public static RespArray ToRespArrayView(this RespAggregate slice)
    {
        if (!RespArray.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespArray(slice, CompletionGuard.Restrictive);
    }

    public static RespMap ToRespMapView(this in RespValueVariant variant)
    {
        if (variant.Aggregate is not { } agg || !RespMap.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespMap(agg, CompletionGuard.Restrictive);
    }

    public static RespMap ToRespMapView(this RespAggregate slice)
    {
        if (!RespMap.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespMap(slice, CompletionGuard.Restrictive);
    }

    public static RespPush ToRespPushView(this in RespValueVariant variant)
    {
        if (variant.Aggregate is not { } agg || !RespPush.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespPush(agg, CompletionGuard.Restrictive);
    }

    public static RespPush ToRespPushView(this RespAggregate slice)
    {
        if (!RespPush.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespPush(slice, CompletionGuard.Restrictive);
    }

    public static RespSet ToRespSetView(this in RespValueVariant variant)
    {
        if (variant.Aggregate is not { } agg || !RespSet.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespSet(agg, CompletionGuard.Restrictive);
    }

    public static RespSet ToRespSetView(this RespAggregate slice)
    {
        if (!RespSet.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespSet(slice, CompletionGuard.Restrictive);
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
                return new RespArray(aggregate, CompletionGuard.Restrictive).ToArrayOf(Decode);
            case RespType.Map:
                return new RespMap(aggregate, CompletionGuard.Restrictive).ToMapWithStringKey();
            case RespType.Set:
                return new RespSet(aggregate, CompletionGuard.Restrictive).ToHashSetOf(Decode);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public static object? ToClrValue(this Frame<RespContext> memory)
    {
        return memory.Context.Type switch
        {
            RespType.None => null,
            RespType.Number => new RespNumber(memory, CompletionGuard.Restrictive).ToInt64(),
            RespType.Null => null,
            RespType.Double => new RespDouble(memory, CompletionGuard.Restrictive).ToDouble(),
            RespType.Boolean => new RespBoolean(memory, CompletionGuard.Restrictive).ToBool(),
            RespType.BulkError or RespType.SimpleError => new Exception(new RespString(memory, CompletionGuard.Restrictive).ToString()),
            RespType.VerbatimString or RespType.BulkString or RespType.SimpleString => new RespString(memory, CompletionGuard.Restrictive).ToString(),
            RespType.BigNumber => new RespBigNumber(memory, CompletionGuard.Restrictive).ToBigInteger(),
            RespType.Array or RespType.Map or RespType.Set or RespType.Attribute or RespType.Push or RespType.SteamedStringChunk or RespType.End
                => throw new ArgumentOutOfRangeException(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}