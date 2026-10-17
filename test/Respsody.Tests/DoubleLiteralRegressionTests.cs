using System;
using Respsody.Client;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class DoubleLiteralRegressionTests
{
    [Theory]
    [InlineData("inf", double.PositiveInfinity)]
    [InlineData("+inf", double.PositiveInfinity)]
    [InlineData("-inf", double.NegativeInfinity)]
    [InlineData("nan", double.NaN)]
    [InlineData("+nan", double.NaN)]
    [InlineData("-nan", double.NaN)]
    [InlineData("-1.25e2", -125d)]
    public void ParsesCompleteDoubleLiterals(string value, double expected)
    {
        using var response = new RespDouble(RegressionFrames.Create("," + value + "\r\n"), new CompletionGuard());
        Assert.Equal(expected, response.ToDouble());
    }

    [Theory]
    [InlineData("inn")]
    [InlineData("abn")]
    [InlineData("+abn")]
    [InlineData("ixf")]
    [InlineData("+xxf")]
    [InlineData("-xxf")]
    [InlineData("")]
    [InlineData("1.5junk")]
    public void RejectsMisspelledDoubleLiterals(string value)
    {
        using var response = new RespDouble(RegressionFrames.Create("," + value + "\r\n"), new CompletionGuard());
        Assert.Throws<FormatException>(() => response.ToDouble());
    }
}
