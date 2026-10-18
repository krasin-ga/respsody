using System;
using System.Collections.Generic;
using System.Text;
using Respsody.Memory;
using Respsody.Resp;
using Respsody.Resp.Parsing;
using Xunit;

namespace Respsody.Tests;

public class FramingResetRegressionTests
{
    [Theory]
    [InlineData("$12")]
    [InlineData("$-")]
    [InlineData("+old\r")]
    [InlineData("$12\r")]
    [InlineData("$12\r\npartial")]
    public void ResetStartsNewProtocolSequence(string interrupted)
    {
        using var framing = new RespFraming(new MemoryBlocks(), 64);
        Assert.Empty(Feed(framing, interrupted));
        framing.Reset();
        var responses = Feed(framing, "$3\r\nabc\r\n+OK\r\n");
        Assert.Equal(new[] { "$3\r\nabc\r\n", "+OK\r\n" }, responses);
    }

    internal static List<string> Feed(RespFraming framing, string value)
    {
        var responses = new List<string>();
        var bytes = Encoding.UTF8.GetBytes(value).AsSpan();
        while (!bytes.IsEmpty)
        {
            using var block = framing.GetReceivingBlock();
            var count = Math.Min(bytes.Length, block.Remaining);
            bytes[..count].CopyTo(block.GetWritableSpan());
            block.Advance(count);
            using var frames = framing.Feed(block);
            foreach (var frame in frames)
                using (frame)
                    responses.Add(Encoding.UTF8.GetString(frame.Span));
            bytes = bytes[count..];
        }
        return responses;
    }
}
