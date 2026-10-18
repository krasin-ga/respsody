using System.Collections.Generic;
using Respsody.Client;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class ArrayEnumeratorRegressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EnumerationSkipsProtocolHeaderAndResetRepeatsValues(int length)
    {
        var aggregate = new RespAggregatesPool().Lease();
        aggregate.Add(RegressionFrames.Create($"*{length}\r\n", length: length));
        var expected = new List<string?>();
        for (var i = 0; i < length; i++)
        {
            aggregate.Add(RegressionFrames.Create($"+value{i}\r\n"));
            expected.Add($"value{i}");
        }
        using var array = new RespArray(aggregate, new CompletionGuard());
        var enumerator = array.EnumerateStrings();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var actual = new List<string?>();
            while (enumerator.MoveNext())
                actual.Add(enumerator.Current.ToString());
            Assert.Equal(expected, actual);
            enumerator.Reset();
        }
    }
}
