using System;
using System.Runtime.CompilerServices;
using Respsody.Memory;
using Xunit;

namespace Respsody.Tests;

public class StandaloneMemoryBlockRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneBlockCanReturnExplicitLeaseWithoutPool(bool suppliedArray)
    {
        var block = suppliedArray ? new MemoryBlock(new byte[64]) : new MemoryBlock(64);
        block.Advance(1);
        block.Lease(0, 1);
        block.ReturnLease();
        Assert.Equal(0, block.Written);
        block.Lease(0, 64);
        block.Dispose();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FinalizingStandaloneBlockDoesNotRequireOwner(bool suppliedArray, bool leased)
    {
        var reference = CreateAbandonedBlock(suppliedArray, leased);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAbandonedBlock(bool suppliedArray, bool leased)
    {
        var block = suppliedArray ? new MemoryBlock(new byte[64]) : new MemoryBlock(64);
        if (leased)
            block.Lease(0, 64);
        return new WeakReference(block);
    }
}
