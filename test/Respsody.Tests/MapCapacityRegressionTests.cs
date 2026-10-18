using System.Collections.Generic;
using Respsody.Client;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class MapCapacityRegressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void MaterializedMapReservesForPairs(int pairs)
    {
        var aggregate = new RespAggregatesPool().Lease();
        aggregate.Add(RegressionFrames.Create($"%{pairs}\r\n", length: pairs));
        for (var i = 0; i < pairs; i++)
        {
            aggregate.Add(RegressionFrames.Create($"+key{i}\r\n"));
            aggregate.Add(RegressionFrames.Create($":{i}\r\n"));
        }
        using var response = new RespMap(aggregate, new CompletionGuard());
        var result = Assert.IsType<Dictionary<string, object?>>(response.ToMapWithStringKey());
        Assert.Equal(pairs, result.Count);
        for (var i = 0; i < pairs; i++)
            Assert.Equal((long)i, result[$"key{i}"]);
        Assert.InRange(result.EnsureCapacity(0), pairs, pairs == 0 ? 0 : pairs * 2 - 1);
    }
}
