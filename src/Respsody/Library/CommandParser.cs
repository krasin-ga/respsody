using Respsody.Client;
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
        var aggregationStrategy = new RespFrameAggregationStrategy(new RespAggregatesPool());
        var disposalGuard = new DisposalGuard(new CompletionGuard(), checkOnly: true);
        foreach (var readyFrame in readyFrames)
        {
            if (!aggregationStrategy.Aggregate(readyFrame, out var value))
                continue;

            if (value.Type != RespType.Array)
                throw new InvalidOperationException("Command must be array of bulk strings");

            using var agg = value.Aggregate!;
            var array = value.Aggregate!.ToRespArrayView(disposalGuard);

            if (array.Length <= 1)
                throw new InvalidOperationException("Command must have arguments");

            static Bytes Decode(in OwnedRespValueVariant valueVariant) =>
                new(valueVariant.ToRespStringView().GetSpan().ToArray());

            return array.ToArrayOf(Decode)[1..];
        }

        throw new InvalidOperationException("Failed to parse arguments");
    }
}