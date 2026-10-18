using System;
using Respsody.Benchmarks.NonScientific.Library;
using Xunit;

namespace Respsody.Tests;

public class BenchmarkDeltaRegressionTests
{
    [Theory]
    [InlineData(668906250L, 668906250L)]
    [InlineData(668906250L, 668906251L)]
    public void NondecreasingCpuSamplesArePreserved(long beforeTicks, long afterTicks)
    {
        var delta = new Delta<TimeSpan>(TimeSpan.FromTicks(beforeTicks), TimeSpan.FromTicks(afterTicks));

        Assert.True(delta.IsIncreasingOrEq);
        Assert.Equal(delta, delta.EnsureIncreasingOrEq());
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1024L, 1024L)]
    [InlineData(1024L, 1025L)]
    public void NondecreasingAllocationSamplesArePreserved(long before, long after)
    {
        var delta = new Delta<long>(before, after);

        Assert.True(delta.IsIncreasingOrEq);
        Assert.Equal(delta, delta.EnsureIncreasingOrEq());
    }

    [Fact]
    public void DecreasingCpuSamplesAreRejected()
    {
        var delta = new Delta<TimeSpan>(TimeSpan.FromTicks(2), TimeSpan.FromTicks(1));

        Assert.False(delta.IsIncreasingOrEq);
        Assert.Throws<InvalidOperationException>(() => delta.EnsureIncreasingOrEq());
    }

    [Fact]
    public void DecreasingAllocationSamplesAreRejected()
    {
        var delta = new Delta<long>(1024, 1023);

        Assert.False(delta.IsIncreasingOrEq);
        Assert.Throws<InvalidOperationException>(() => delta.EnsureIncreasingOrEq());
    }
}
