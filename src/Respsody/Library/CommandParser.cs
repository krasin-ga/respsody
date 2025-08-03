using Respsody.Memory;
using Respsody.Resp;
using Respsody.Resp.Parsing;

namespace Respsody.Library;

internal static class CommandParser
{
    public static Bytes[] ParseArgumentsSlow<T>(Command<T> command)
        where T : IRespResponse
    {
        var segmentList = command.OutgoingBuffer.AsSegmentList();
        var arraySize = 0;

        for (var i = 0; i < segmentList.Count; i++)
            arraySize += segmentList[i].Count;

        using var framing = new RespFraming(new MemoryBlocks(new NoOpPool()), arraySize);
        using var block = framing.GetReceivingBlock();
        var writableMemory = block.GetWritableMemory();

        for (var i = 0; i < segmentList.Count; i++)
        {
            var segmentSpan = segmentList[i].AsSpan();
            segmentSpan.CopyTo(writableMemory.Span);
            writableMemory = writableMemory[segmentSpan.Length..];
            block.Advance(segmentSpan.Length);
        }

        using var readyFrames = framing.Feed(block);
        var agg = new RespFrameAggregationStrategy(new RespAggregatesPool());

        foreach (var readyFrame in readyFrames)
        {
            if (!agg.Aggregate(readyFrame, out var value))
                continue;

            if (value.Type != RespType.Array)
                throw new InvalidOperationException("Command must be array of bulk strings");

            using var array = value.Aggregate!.ToRespArray();

            if (array.Length <= 1)
                throw new InvalidOperationException("Command must have arguments");

            static Bytes Decode(in RespValueVariant valueVariant) =>
                new(valueVariant.Simple!.Value.ToRespString().GetSpan().ToArray());

            return array.ToArrayOf(Decode)[1..];
        }

        throw new InvalidOperationException("Failed to parse arguments");
    }
}