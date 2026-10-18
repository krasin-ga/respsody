using System;
using System.Globalization;
using System.Numerics;
using Respsody.Client;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class BigNumberRegressionTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("+42")]
    [InlineData("-1234567890123456789012345678901234567890")]
    [InlineData("1234567890123456789012345678901234567890")]
    public void ParsesSignedBigIntegers(string value)
    {
        using var response = new RespBigNumber(RegressionFrames.Create("(" + value + "\r\n"), new CompletionGuard());
        Assert.Equal(BigInteger.Parse(value, CultureInfo.InvariantCulture), response.ToBigInteger());
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("1x")]
    [InlineData("1.5")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("--1")]
    public void RejectsMalformedBigIntegers(string value)
    {
        using var response = new RespBigNumber(RegressionFrames.Create("(" + value + "\r\n"), new CompletionGuard());
        Assert.Throws<FormatException>(() => response.ToBigInteger());
    }
}
