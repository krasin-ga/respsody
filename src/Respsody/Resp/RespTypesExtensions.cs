using Respsody.Client;
using Respsody.Memory;
using System.Runtime.CompilerServices;

namespace Respsody.Resp;

public static class RespTypesExtensions
{
    public static RespString ToRespStringView(this in OwnedRespValueVariant ownedRespValueVariant)
    {
        var variant = ownedRespValueVariant.Variant;
        if (variant.Simple is not { } simple || !RespString.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespString(simple, ownedRespValueVariant.Guard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static RespString ToRespStringView(this in OwnedRespFrame ownedFrame)
    {
        var frame = ownedFrame.Frame;
        if (!RespString.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespString(frame, ownedFrame.Guard);
    }

    public static RespDouble ToRespDoubleView(this in OwnedRespValueVariant ownedRespValueVariant)
    {
        var variant = ownedRespValueVariant.Variant;

        if (variant.Simple is not { } simple || !RespDouble.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespDouble(simple, ownedRespValueVariant.Guard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static RespDouble ToRespDoubleView(this in OwnedRespFrame ownedFrame)
    {
        var frame = ownedFrame.Frame;
        if (!RespDouble.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespDouble(frame, ownedFrame.Guard);
    }

    public static RespNumber ToRespNumberView(this in OwnedRespValueVariant ownedRespValueVariant)
    {
        var variant = ownedRespValueVariant.Variant;

        if (variant.Simple is not { } simple || !RespNumber.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespNumber(simple, ownedRespValueVariant.Guard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static RespNumber ToRespNumberView(this in OwnedRespFrame ownedFrame)
    {
        var frame = ownedFrame.Frame;
        if (!RespNumber.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespNumber(frame, ownedFrame.Guard);
    }

    public static RespBigNumber ToRespBigNumberView(this in OwnedRespValueVariant ownedRespValueVariant)
    {
        var variant = ownedRespValueVariant.Variant;

        if (variant.Simple is not { } simple || !RespBigNumber.CanConvert(simple))
            throw CreateConversionException(variant);

        return new RespBigNumber(simple, ownedRespValueVariant.Guard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static RespBigNumber ToRespBigNumberView(this in OwnedRespFrame ownedFrame)
    {
        var frame = ownedFrame.Frame;
        if (!RespBigNumber.CanConvert(frame))
            throw CreateConversionException(frame);

        return new RespBigNumber(frame, ownedFrame.Guard);
    }

    public static RespArray ToRespArrayView(this in OwnedRespValueVariant ownedRespValueVariant)
    {
        var variant = ownedRespValueVariant.Variant;

        if (variant.Aggregate is not { } agg || !RespArray.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespArray(agg, ownedRespValueVariant.Guard);
    }

    public static RespMap ToRespMapView(this in OwnedRespValueVariant ownedRespValueVariant)
    {
        var variant = ownedRespValueVariant.Variant;

        if (variant.Aggregate is not { } agg || !RespMap.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespMap(agg, ownedRespValueVariant.Guard);
    }


    public static RespSet ToRespSetView(this in OwnedRespValueVariant ownedRespValueVariant)
    {
        var variant = ownedRespValueVariant.Variant;

        if (variant.Aggregate is not { } agg || !RespSet.CanConvert(agg.HeaderFrame))
            throw CreateConversionException(variant);

        return new RespSet(agg, ownedRespValueVariant.Guard);
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

    public static object? ToClrValue(this OwnedRespValueVariant ownedRespValueVariant)
    {
        var valueVariant = ownedRespValueVariant.Variant;

        if (valueVariant.Simple.HasValue)
            return valueVariant.Simple.Value.ToClrValue(ownedRespValueVariant.Guard);

        var aggregate = valueVariant.Aggregate;
        if (aggregate is null)
            return null;

        static object? Decode(in OwnedRespValueVariant v)
        {
            return v.ToClrValue();
        }

        switch (valueVariant.Type)
        {
            case RespType.Array:
                return new RespArray(aggregate, ownedRespValueVariant.Guard).ToArrayOf(Decode);
            case RespType.Map:
                return new RespMap(aggregate, ownedRespValueVariant.Guard).ToMapWithStringKey();
            case RespType.Set:
                return new RespSet(aggregate, ownedRespValueVariant.Guard).ToHashSetOf(Decode);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public static object? ToClrValue(this Frame<RespContext> memory, DisposalGuard guard)
    {
        return memory.Context.Type switch
        {
            RespType.None => null,
            RespType.Number => new RespNumber(memory, guard.ToCheckOnly()).ToInt64(),
            RespType.Null => null,
            RespType.Double => new RespDouble(memory, guard.ToCheckOnly()).ToDouble(),
            RespType.Boolean => new RespBoolean(memory, guard.ToCheckOnly()).ToBool(),
            RespType.BulkError or RespType.SimpleError => new Exception(new RespString(memory, guard.ToCheckOnly())
                .ToString()),
            RespType.VerbatimString or RespType.BulkString or RespType.SimpleString => new RespString(memory,
                guard.ToCheckOnly()).ToString(),
            RespType.BigNumber => new RespBigNumber(memory, guard.ToCheckOnly()).ToBigInteger(),
            RespType.Array or RespType.Map or RespType.Set or RespType.Attribute or RespType.Push
                or RespType.SteamedStringChunk or RespType.End
                => throw new ArgumentOutOfRangeException(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    internal static RespArray ToRespArrayView(this RespAggregate slice, DisposalGuard guard)
    {
        if (!RespArray.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespArray(slice, guard.ToCheckOnly());
    }

    internal static RespMap ToRespMapView(this RespAggregate slice, DisposalGuard guard)
    {
        if (!RespMap.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespMap(slice, guard.ToCheckOnly());
    }

    internal static RespPush ToRespPushView(this RespAggregate slice, DisposalGuard guard)
    {
        if (!RespPush.CanConvert(slice.HeaderFrame))
            throw CreateConversionException(slice.HeaderFrame);

        return new RespPush(slice, guard);
    }
}