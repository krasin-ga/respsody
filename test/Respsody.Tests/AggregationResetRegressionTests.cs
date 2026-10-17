using System.Collections.Generic;
using Respsody.Library;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class AggregationResetRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetReleasesIncompleteNestedAggregatesAndStream(bool completedChild)
    {
        var strategy = new RespFrameAggregationStrategy(new RespAggregatesPool());
        var lifetimes = new List<RegressionFrames.Counter>();
        Add("*3\r\n", 3);
        Add("*2\r\n", 2);
        Add(":1\r\n", 0);
        if (completedChild)
            Add(":2\r\n", 0);
        Add("$?\r\n", Constants.StreamedLength);
        Add(";3\r\nabc\r\n", 3);

        strategy.Reset();
        strategy.Reset();
        Assert.All(lifetimes, lifetime => Assert.Equal(1, lifetime.Disposals));
        var scalarLifetime = new RegressionFrames.Counter();
        Assert.True(strategy.Aggregate(RegressionFrames.Create("+OK\r\n", scalarLifetime), out var result));
        result.Dispose();
        Assert.Equal(1, scalarLifetime.Disposals);
        return;

        void Add(string frame, int length)
        {
            var lifetime = new RegressionFrames.Counter();
            lifetimes.Add(lifetime);
            Assert.False(strategy.Aggregate(RegressionFrames.Create(frame, lifetime, length), out _));
        }
    }

    [Fact]
    public void ResetDoesNotDisposeCompletedNestedResponse()
    {
        var strategy = new RespFrameAggregationStrategy(new RespAggregatesPool());
        var lifetimes = new[] { new RegressionFrames.Counter(), new RegressionFrames.Counter(), new RegressionFrames.Counter() };
        Assert.False(strategy.Aggregate(RegressionFrames.Create("*1\r\n", lifetimes[0], 1), out _));
        Assert.False(strategy.Aggregate(RegressionFrames.Create("*1\r\n", lifetimes[1], 1), out _));
        Assert.True(strategy.Aggregate(RegressionFrames.Create(":1\r\n", lifetimes[2]), out var result));
        strategy.Reset();
        Assert.All(lifetimes, lifetime => Assert.Equal(0, lifetime.Disposals));
        result.Dispose();
        Assert.All(lifetimes, lifetime => Assert.Equal(1, lifetime.Disposals));
    }
}
