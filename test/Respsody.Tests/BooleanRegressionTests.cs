using System;
using Respsody.Client;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class BooleanRegressionTests
{
    [Theory]
    [InlineData("t", true)]
    [InlineData("f", false)]
    public void ParsesBooleanLiterals(string value, bool expected)
    {
        using var response = new RespBoolean(RegressionFrames.Create("#" + value + "\r\n"), new CompletionGuard());
        Assert.Equal(expected, response.ToBool());
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("T")]
    [InlineData("F")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("tt")]
    public void RejectsMalformedBooleans(string value)
    {
        using var response = new RespBoolean(RegressionFrames.Create("#" + value + "\r\n"), new CompletionGuard());
        Assert.Throws<FormatException>(() => response.ToBool());
    }
}
