using Respsody.Memory;
using Xunit;

namespace Respsody.Tests;

public class MemoryPoolCapacityRegressionTests
{
    [Theory]
    [InlineData(0, 64)]
    [InlineData(1, 64)]
    [InlineData(63, 64)]
    [InlineData(65, 128)]
    [InlineData(129, 256)]
    [InlineData(257, 512)]
    [InlineData(513, 1024)]
    public void ReusedBucketHasCapacityForEveryRequest(int requested, int capacity)
    {
        var blocks = new MemoryBlocks();
        var original = blocks.Lease(requested);
        Assert.Equal(capacity, original.Size);
        original.Dispose();
        using var reused = blocks.Lease(capacity);
        Assert.Same(original, reused);
        Assert.Equal(capacity, reused.GetWritableSpan().Length);
    }

    [Fact]
    public void CreationEventsReportAllocatedCapacity()
    {
        var blocks = new MemoryBlocks();
        var created = 0;
        var handlers = blocks.AddEventHandlers(size => created += size, _ => { }, _ => { });
        using var block = blocks.Lease(65);
        Assert.Equal(block.Size, created);
        System.GC.KeepAlive(handlers);
    }

    [Fact]
    public void ExternallySuppliedUndersizedBlockCannotPolluteBucket()
    {
        var pool = new StandardMemoryBlocksPool();
        var external = new MemoryBlocks(new NoOpPool()).Lease(65);
        pool.Return(external);
        using var leased = new MemoryBlocks(pool).Lease(128);
        Assert.NotSame(external, leased);
        Assert.True(leased.Size >= 128);
        external.Dispose();
    }
}
