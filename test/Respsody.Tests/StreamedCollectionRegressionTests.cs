using System.Collections.Generic;
using Respsody.Library;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class StreamedCollectionRegressionTests
{
    [Theory]
    [InlineData("*", 0)]
    [InlineData("*", 2)]
    [InlineData("~", 0)]
    [InlineData("~", 2)]
    [InlineData("%", 0)]
    [InlineData("%", 2)]
    public void StreamedCollectionExcludesAndDisposesEndFrame(string type, int count)
    {
        var strategy = new RespFrameAggregationStrategy(new RespAggregatesPool());
        var header = new RegressionFrames.Counter();
        var lifetimes = new List<RegressionFrames.Counter> { header };
        Assert.False(strategy.Aggregate(RegressionFrames.Create(type + "?\r\n", header, Constants.StreamedLength), out _));
        for (var i = 0; i < count; i++)
        {
            var lifetime = new RegressionFrames.Counter();
            lifetimes.Add(lifetime);
            Assert.False(strategy.Aggregate(RegressionFrames.Create($":{i}\r\n", lifetime), out _));
        }

        var end = new RegressionFrames.Counter();
        Assert.True(strategy.Aggregate(RegressionFrames.Create(".\r\n", end), out var result));
        Assert.Equal(count + 1, result.Aggregate!.Length);
        Assert.Equal(1, end.Disposals);
        Assert.All(lifetimes, lifetime => Assert.Equal(0, lifetime.Disposals));
        result.Dispose();
        Assert.All(lifetimes, lifetime => Assert.Equal(1, lifetime.Disposals));
        Assert.Equal(1, end.Disposals);
    }

    [Fact]
    public void StreamedChildCompletesItsFixedLengthParent()
    {
        var strategy = new RespFrameAggregationStrategy(new RespAggregatesPool());
        Assert.False(strategy.Aggregate(RegressionFrames.Create("*1\r\n", length: 1), out _));
        Assert.False(strategy.Aggregate(RegressionFrames.Create("*?\r\n", length: Constants.StreamedLength), out _));
        Assert.False(strategy.Aggregate(RegressionFrames.Create(":1\r\n"), out _));
        Assert.True(strategy.Aggregate(RegressionFrames.Create(".\r\n"), out var result));
        using (result)
        {
            Assert.Equal(2, result.Aggregate!.Length);
            Assert.Equal(2, result.Aggregate[1].Aggregate!.Length);
        }
    }
}
