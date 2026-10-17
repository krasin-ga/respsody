using System;
using System.Text;
using Respsody.Client;
using Respsody.Memory;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class RespNumberRegressionTests
{
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("+1", 1L)]
    [InlineData("-0", 0L)]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("-2147483649", -2147483649L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void ParsesSigned64BitRange(string value, long expected)
    {
        using var number = new RespNumber(RegressionFrames.Create(":" + value + "\r\n"), new CompletionGuard());
        Assert.Equal(expected, number.ToInt64());
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("12a")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.0")]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    public void RejectsMalformedOrOverflowingIntegers(string value)
    {
        using var number = new RespNumber(RegressionFrames.Create(":" + value + "\r\n"), new CompletionGuard());
        Assert.Throws<FormatException>(() => number.ToInt64());
    }
}

internal static class RegressionFrames
{
    internal static Frame<RespContext> Create(string value, IDisposable? lifetime = null, int length = 0, int dataOffset = 1)
        => new(new RespContext { Type = (RespType)value[0], Length = length, DataOffset = dataOffset },
            Encoding.UTF8.GetBytes(value), lifetime ?? new Counter());

    internal sealed class Counter : IDisposable
    {
        internal int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
}
