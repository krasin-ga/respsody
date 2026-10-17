using Respsody.Library;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class StreamedStringOwnershipRegressionTests
{
    [Fact]
    public void StreamTerminatorReturnsItsLeaseWithoutBecomingData()
    {
        var strategy = new RespFrameAggregationStrategy(new RespAggregatesPool());
        var header = new RegressionFrames.Counter();
        var chunk = new RegressionFrames.Counter();
        var terminator = new RegressionFrames.Counter();
        Assert.False(strategy.Aggregate(RegressionFrames.Create("$?\r\n", header, Constants.StreamedLength), out _));
        Assert.False(strategy.Aggregate(RegressionFrames.Create(";3\r\nabc\r\n", chunk, 3), out _));
        Assert.True(strategy.Aggregate(RegressionFrames.Create(";0\r\n", terminator), out var value));
        Assert.Equal(1, terminator.Disposals);
        Assert.Equal(0, header.Disposals);
        Assert.Equal(0, chunk.Disposals);
        Assert.Equal(2, value.Aggregate!.Length);
        value.Dispose();
        Assert.Equal(1, header.Disposals);
        Assert.Equal(1, chunk.Disposals);
        Assert.Equal(1, terminator.Disposals);
    }
}
