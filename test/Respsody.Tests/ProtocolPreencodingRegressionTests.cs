using System;
using System.Text;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class ProtocolPreencodingRegressionTests
{
    [Theory]
    [InlineData("", "$0\r\n\r\n")]
    [InlineData("SET", "$3\r\nSET\r\n")]
    [InlineData("é", "$2\r\né\r\n")]
    [InlineData("😀", "$4\r\n😀\r\n")]
    [InlineData("a\r\nb", "$4\r\na\r\nb\r\n")]
    public void PreencodesUtf8WithExactByteLength(string value, string expected)
        => Assert.Equal(Encoding.UTF8.GetBytes(expected), ProtocolWriter.ConvertToBulkString(value));

    [Fact]
    public void PreencodingAllocatesOnlyTheFinalByteArray()
    {
        var length = ProtocolWriter.ConvertToBulkString("SET").Length;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var written = 0;
        for (var i = 0; i < 1000; i++)
            written += ProtocolWriter.ConvertToBulkString("SET").Length;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(length * 1000, written);
        Assert.InRange(allocated, length * 1000L, (length + 32L) * 1000);
    }
}
