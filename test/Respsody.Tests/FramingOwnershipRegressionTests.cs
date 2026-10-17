using System;
using System.Collections.Generic;
using System.Text;
using Respsody.Memory;
using Respsody.Resp;
using Respsody.Resp.Parsing;
using Xunit;

namespace Respsody.Tests;

public class FramingOwnershipRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbandoningFragmentedFrameReturnsEveryLease(bool reset)
    {
        var pool = new TrackingMemoryPool();
        var framing = new RespFraming(new MemoryBlocks(pool), 64);
        Assert.Empty(FramingResetRegressionTests.Feed(framing, "$200\r\n" + new string('a', 130)));
        Assert.Equal(3, pool.Outstanding);
        if (reset)
        {
            framing.Reset();
            Assert.Equal(1, pool.Outstanding);
            Assert.Equal(new[] { "+OK\r\n" }, FramingResetRegressionTests.Feed(framing, "+OK\r\n"));
        }

        framing.Dispose();
        framing.Dispose();
        Assert.Equal(0, pool.Outstanding);
        Assert.Throws<ObjectDisposedException>(() => framing.Reset());
    }

    [Fact]
    public void ResetPreservesOwnershipOfPreviouslyReturnedFrame()
    {
        var pool = new TrackingMemoryPool();
        using var framing = new RespFraming(new MemoryBlocks(pool), 64);
        Frame<RespContext> retained = default;
        using (var block = framing.GetReceivingBlock())
        {
            "+OK\r\n"u8.CopyTo(block.GetWritableSpan());
            block.Advance(5);
            using var frames = framing.Feed(block);
            foreach (var frame in frames)
                retained = frame;
        }

        framing.Reset();
        Assert.Equal(2, pool.Outstanding);
        Assert.Equal("+OK\r\n", Encoding.UTF8.GetString(retained.Span));
        retained.Dispose();
        Assert.Equal(1, pool.Outstanding);
    }
}

internal sealed class TrackingMemoryPool : IMemoryBlocksPool
{
    private readonly HashSet<MemoryBlock> _leased = new();
    internal int Outstanding => _leased.Count;

    public (MemoryBlock Block, bool IsNewlyCreated) Lease(int blockSize)
    {
        var block = new MemoryBlock(blockSize);
        Assert.True(_leased.Add(block));
        return (block, true);
    }

    public void Return(MemoryBlock block) => Assert.True(_leased.Remove(block), "Lease returned twice.");
}
