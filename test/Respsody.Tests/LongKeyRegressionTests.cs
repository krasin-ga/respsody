using System;
using System.Linq;
using System.Text;
using Respsody.Memory;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class LongKeyRegressionTests
{
    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    public void HashingUsesWrittenUtf8Bytes(int length)
    {
        var value = new string('a', length - 1) + "é";
        var expected = Key.ByteArray(Encoding.UTF8.GetBytes(value)).CalculateHashSlot();
        Assert.Equal(expected, Key.Utf8(value).CalculateHashSlot());
    }

    [Theory]
    [InlineData(257, false)]
    [InlineData(257, true)]
    [InlineData(512, false)]
    [InlineData(512, true)]
    [InlineData(513, false)]
    [InlineData(513, true)]
    public void FragmentedSerializationPreservesBytesAndHashSlot(int length, bool calculate)
    {
        var value = new string('a', length);
        var key = Key.Utf8(value);
        var buffer = new MemoryBlocks().LeaseLinked(64, false, 0);
        var current = buffer.GetCurrentBlock();
        var prefixLength = current.Remaining;
        buffer.Write(new byte[prefixLength]);
        if (calculate)
            Assert.Equal(Key.ByteArray(Encoding.UTF8.GetBytes(value)).CalculateHashSlot(), key.WriteAndCalculateHashSlot(buffer));
        else
            key.WriteTo(buffer);
        buffer.Commit();
        try
        {
            var segments = buffer.AsSegmentList();
            var bytes = Enumerable.Range(0, segments.Count).SelectMany(i => segments[i]).ToArray();
            Assert.Equal(ProtocolWriter.ConvertToBulkString(value), bytes.AsSpan(prefixLength).ToArray());
        }
        finally
        {
            buffer.FreeByOwner();
            buffer.FreeByDeliveryPipeline();
        }
    }
}
