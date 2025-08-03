using System.Runtime.CompilerServices;
using Respsody.Library;
using Respsody.Memory;

namespace Respsody.Resp;

public class RespFrameAggregationStrategy(RespAggregatesPool slicesPool)
{
    private AggregateFrame[] _stack = new AggregateFrame[32];

    private int _stackPosition = -1;
    private RespAggregate? _streamedString;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool Aggregate(
        Frame<RespContext> frame,
        out RespValueVariant value)
    {
        var context = frame.Context;
        if (context.Type.IsCollection())
        {
            var aggregate = slicesPool.Lease();
            aggregate.Add(frame);

            var remainingLength = frame.Context.Type is RespType.Map or RespType.Attribute
                ? context.Length * 2
                : context.Length;

            if (remainingLength == 0)
                return AddOrReturn(new RespValueVariant(aggregate), out value);

            var nextPosition = ++_stackPosition;
            if (nextPosition == _stack.Length)
                Array.Resize(ref _stack, _stack.Length * 2);

            _stack[nextPosition] = new AggregateFrame(
                aggregate,
                remainingLength);

            value = default;
            return false;
        }

        if (context is { Type: RespType.BulkString, Length: Constants.StreamedLength })
        {
            _streamedString = slicesPool.Lease();
            _streamedString.Add(frame);

            value = default;
            return false;
        }

        if (context.Type == RespType.SteamedStringChunk)
        {
            if (_streamedString is null)
                throw new InvalidOperationException();

            if (context.Length != 0)
            {
                _streamedString.Add(frame);
                value = default;
                return false;
            }

            var streamedString = _streamedString;
            _streamedString = null;

            return AddOrReturn(new RespValueVariant(streamedString), out value);
        }

        return AddOrReturn(new RespValueVariant(frame), out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool AddOrReturn(
        RespValueVariant variant,
        out RespValueVariant respValueVariant)
    {
        while (true)
        {
            if (_stackPosition > -1)
            {
                ref var element = ref _stack[_stackPosition];

                element.Aggregate.Add(variant);

                var remaining = --element.Remaining;
                if (remaining == 0 ||
                    remaining < Constants.StreamedLength
                    && variant.Simple?.Context.Type == RespType.End)
                    if (--_stackPosition == -1)
                    {
                        respValueVariant = new RespValueVariant(element.Aggregate);
                        element = default;
                        return true;
                    }
                    else
                    {
                        variant = new RespValueVariant(element.Aggregate);
                        continue;
                    }

                respValueVariant = default;
                return false;
            }

            respValueVariant = variant;
            return true;
        }
    }

    public void Reset()
    {
        _stackPosition = -1;
        _streamedString = null;
    }

    private struct AggregateFrame(RespAggregate aggregate, int remaining)
    {
        public readonly RespAggregate Aggregate = aggregate;
        public int Remaining = remaining;
    }
}