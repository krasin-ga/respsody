using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Respsody.Memory;
using Xunit;

namespace Respsody.Tests;

public class MemoryPoolAccountingRegressionTests
{
    [Fact]
    public void RejectingOneBlockPreservesRetainedBytes()
    {
        var pool = new StandardMemoryBlocksPool(128);
        var blocks = new MemoryBlocks(pool);
        var leased = Enumerable.Range(0, 3).Select(_ => blocks.Lease(64)).ToArray();
        foreach (var block in leased)
            block.Dispose();

        Assert.Equal(128, RetainedBytes(pool));
        using var first = blocks.Lease(64);
        using var second = blocks.Lease(64);
        using var third = blocks.Lease(64);
        Assert.Contains(first, leased);
        Assert.Contains(second, leased);
        Assert.DoesNotContain(third, leased);
        Assert.Equal(0, RetainedBytes(pool));
    }

    [Fact]
    public void ConcurrentReturnsStayWithinBudgetAndMatchStoredBlocks()
    {
        const int budget = 8 * 64;
        var pool = new StandardMemoryBlocksPool(budget);
        var blocks = new MemoryBlocks(pool);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var leased = Enumerable.Range(0, 64).Select(_ => blocks.Lease(64)).ToArray();
            Parallel.ForEach(leased, block => block.Dispose());
            var retained = RetainedBytes(pool);
            Assert.InRange(retained, 0, budget);

            var next = Enumerable.Range(0, 64).Select(_ => blocks.Lease(64)).ToArray();
            Assert.Equal(retained, next.Count(leased.Contains) * 64L);
            Assert.Equal(0, RetainedBytes(pool));
            foreach (var block in next)
                block.Dispose();
        }
    }

    private static long RetainedBytes(StandardMemoryBlocksPool pool)
        => (long)typeof(StandardMemoryBlocksPool)
            .GetField("_accumulatedBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;
}
