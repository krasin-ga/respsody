using System;
using System.Text;
using Xunit;

namespace Respsody.Tests;

public class HashSlotTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("123456789", 12739)]
    [InlineData("foo", 12182)]
    [InlineData("bar", 5061)]
    [InlineData("{user1000}.following", 3443)]
    [InlineData("{user1000}.followers", 3443)]
    public void MatchesRedisClusterSlots(string text, int expected)
    {
        Assert.Equal(expected, Key.Utf8(text).CalculateHashSlot());
        Assert.Equal(expected, new Key(Encoding.UTF8.GetBytes(text)).CalculateHashSlot());
    }

    [Theory]
    [InlineData("foo{}{bar}", "foo{}{bar}")]
    [InlineData("foo{{bar}}zap", "{bar")]
    [InlineData("foo{bar}{zap}", "bar")]
    [InlineData("foo{bar", "foo{bar")]
    [InlineData("foo}bar{", "foo}bar{")]
    [InlineData("{你好}:é", "你好")]
    public void UsesFirstNonemptyBracePairOrWholeKey(string key, string hashed)
    {
        Assert.Equal(ReferenceSlot(Encoding.UTF8.GetBytes(hashed)), Key.Utf8(key).CalculateHashSlot());
    }

    [Fact]
    public void SupportsBinaryHashTags()
    {
        byte[] key = [0xFF, (byte)'{', 0, 0x80, (byte)'}', 0xFE];
        Assert.Equal(ReferenceSlot([0, 0x80]), new Key(key).CalculateHashSlot());
    }

    private static ushort ReferenceSlot(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0;
        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return (ushort)(crc & 16383);
    }
}
