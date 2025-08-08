using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Respsody.Memory;
using Respsody.Resp;
using Respsody.Resp.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Respsody.Tests;

public class RespFramingTest(ITestOutputHelper testOutputHelper)
{
    [Theory]
    [InlineData("$0\r\n\r\n", "['$0\r\n\r\n']")]
    [InlineData("-MOVED 15113 192.168.208.4:7002\r\n", "['-MOVED 15113 192.168.208.4:7002\r\n']")]
    [InlineData("*?\r\n:1\r\n:2\r\n:3\r\n.\r\n", "['*?\r\n', ':1\r\n', ':2\r\n', ':3\r\n', '.\r\n']")]
    [InlineData("_\r\n_\r\n_\r\n_\r\n", "['_\r\n', '_\r\n', '_\r\n', '_\r\n']")]
    [InlineData("$-1\r\n$-1\r\n", "['$-1\r\n', '$-1\r\n']")]
    [InlineData("%2\r\n+first\r\n:1\r\n+second\r\n:2\r\n",
                "['%2\r\n', '+first\r\n', ':1\r\n', '+second\r\n', ':2\r\n']")]
    [InlineData("$11\r\nhello world\r\n", "['$11\r\nhello world\r\n']")]
    [InlineData("!21\r\nSYNTAX invalid syntax\r\n", "['!21\r\nSYNTAX invalid syntax\r\n']")]
    [InlineData("*3\r\n:1\r\n:2\r\n:3\r\n", "['*3\r\n', ':1\r\n', ':2\r\n', ':3\r\n']")]
    [InlineData("$?\r\n;4\r\nHell\r\n;5\r\no wor\r\n;1\r\nd\r\n;0\r\n",
                "['$?\r\n', ';4\r\nHell\r\n', ';5\r\no wor\r\n', ';1\r\nd\r\n', ';0\r\n']")]
    [InlineData("*2\r\n*3\r\n:1\r\n$5\r\nhello\r\n:2\r\n#f\r\n",
                "['*2\r\n', '*3\r\n', ':1\r\n', '$5\r\nhello\r\n', ':2\r\n', '#f\r\n']")]
    [InlineData("|1\r\n+key-popularity\r\n%2\r\n$1\r\na\r\n,0.1923\r\n$1\r\nb\r\n,0.0012\r\n*2\r\n:2039123\r\n:9543892\r\n",
                "['|1\r\n', '+key-popularity\r\n', '%2\r\n', '$1\r\na\r\n', ',0.1923\r\n', '$1\r\nb\r\n', ',0.0012\r\n', '*2\r\n', ':2039123\r\n', ':9543892\r\n']")]
    public void Must_correctly_slice_input(string input, string expected)
    {
        var expectations = JsonSerializer.Deserialize<string[]>(
            expected.Replace("\n", "\\n").Replace("\r", "\\r").Replace("'", "\""));

        var inputBytes = Encoding.UTF8.GetBytes(input).AsSpan();

        var respControlSequence = new RespFraming(new MemoryBlocks(), receivingBlockSize: 1024 * 16);
        var actual = new List<string>();

        for (var writingBlockSize = 1; writingBlockSize <= 128; writingBlockSize++)
        {
            var bytes = inputBytes;
            var leftToWrite = bytes.Length;

            while (leftToWrite > 0)
            {
                var memoryBlock = respControlSequence.GetReceivingBlock();
                var memory = memoryBlock.GetWritableMemory();
                var toWrite = Math.Min(Math.Min(leftToWrite, memory.Length), writingBlockSize);

                bytes[..toWrite].CopyTo(memory.Span);
                memoryBlock.Advance(toWrite);
                using var sequences = respControlSequence.Feed(memoryBlock);
                foreach (var sequence in sequences)
                    using (sequence)
                        actual.Add(Encoding.ASCII.GetString(sequence.Memory.Span));

                leftToWrite -= toWrite;
                bytes = bytes[toWrite..];
            }

            Assert.Equal(expectations, actual);
            actual.Clear();
        }
    }

    [Fact]
    public void Must_correctly_slice_input_variations()
    {
        var testCases = Enumerable.Range(0, 2048 * 2).Select(i => new string((char)i, i + 1)).ToArray();

        foreach (var testCase in testCases)
        {
            var byteCount = Encoding.Unicode.GetByteCount(testCase);
            var inputBytes = Encoding.UTF8.GetBytes($"${byteCount}\r\n")
                .Concat(Encoding.Unicode.GetBytes(testCase))
                .Concat("\r\n"u8.ToArray())
                .ToArray();

            var respControlSequence = new RespFraming(new MemoryBlocks(), receivingBlockSize: 1024);
            var actual = new List<string>();
            for (var writingBlockSize = 1; writingBlockSize <= 32; writingBlockSize++)
            {
                var bytes = inputBytes;
                var leftToWrite = bytes.Length;

                while (leftToWrite > 0)
                {
                    var memoryBlock = respControlSequence.GetReceivingBlock();
                    var memory = memoryBlock.GetWritableMemory();
                    var toWrite = Math.Min(Math.Min(leftToWrite, memory.Length), writingBlockSize);

                    bytes[..toWrite].CopyTo(memory.Span);
                    memoryBlock.Advance(toWrite);
                    using var sequences = respControlSequence.Feed(memoryBlock);
                    foreach (var sequence in sequences)
                    {
                        using (sequence)
                            actual.Add(Encoding.Unicode.GetString(sequence.Memory.Span.Slice(sequence.Context.DataOffset, sequence.Context.Length)));
                    }

                    leftToWrite -= toWrite;
                    bytes = bytes[toWrite..];
                }

                try
                {
                    Assert.Equal([testCase], actual);
                }
                catch
                {
                    testOutputHelper.WriteLine(Encoding.UTF8.GetString(inputBytes));
                    testOutputHelper.WriteLine(testCase);
                    throw;
                }

                actual.Clear();
            }
        }
    }

    [Theory]

    [InlineData("%2\r\n+first\r\n:1\r\n+second\r\n:2\r\n",
                 "['%2\r\n', '+first\r\n', ':1\r\n', '+second\r\n', ':2\r\n']")]

    [InlineData("*3\r\n:1\r\n:2\r\n:3\r\n", "['*3\r\n', ':1\r\n', ':2\r\n', ':3\r\n']")]

    [InlineData("*2\r\n*3\r\n:1\r\n$5\r\nhello\r\n:2\r\n#f\r\n",
                 "['*2\r\n', '[*3\r\n, :1\r\n, $5\r\nhello\r\n, :2\r\n]', '#f\r\n']")]
    [InlineData("|1\r\n+key-popularity\r\n%2\r\n$1\r\na\r\n,0.1923\r\n$1\r\nb\r\n,0.0012\r\n*2\r\n:2039123\r\n:9543892\r\n",
                "['|1\r\n', '+key-popularity\r\n', '[%2\r\n, $1\r\na\r\n, ,0.1923\r\n, $1\r\nb\r\n, ,0.0012\r\n]', '*2\r\n', ':2039123\r\n', ':9543892\r\n']")]
    public void Must_correctly_aggregate_slice_input(string input, string expected)
    {
        var expectations = JsonSerializer.Deserialize<string[]>(
            expected.Replace("\n", "\\n").Replace("\r", "\\r").Replace("'", "\""));

        var inputBytes = Encoding.UTF8.GetBytes(input).AsSpan();

        var respControlSequence = new RespFraming(new MemoryBlocks(), receivingBlockSize: 1024 * 16);

        var writingBlockSize = 128;
        var bytes = inputBytes;
        var leftToWrite = bytes.Length;

        var sag = new RespFrameAggregationStrategy(
            new RespAggregatesPool());

        var variants = new List<RespValueVariant>();

        while (leftToWrite > 0)
        {
            var memoryBlock = respControlSequence.GetReceivingBlock();
            var memory = memoryBlock.GetWritableMemory();
            var toWrite = Math.Min(Math.Min(leftToWrite, memory.Length), writingBlockSize);

            bytes[..toWrite].CopyTo(memory.Span);
            memoryBlock.Advance(toWrite);
            using var sliced = respControlSequence.Feed(memoryBlock);

            foreach (var slice in sliced)
                if (sag.Aggregate(slice, out var variant))
                    variants.Add(variant);

            leftToWrite -= toWrite;
            bytes = bytes[toWrite..];
        }

        var actual = new List<string>();
        foreach (var variant in variants)
        {
            if (variant.Aggregate is not { } agg)
                throw new InvalidOperationException();

            for (var i = 0; i < agg.Length; i++)
                actual.Add(agg[i].ToDebugString());
        }

        try
        {
            Assert.Equal(expectations, actual);
        }
        catch
        {
            testOutputHelper.WriteLine(Encoding.UTF8.GetString(inputBytes));
            throw;
        }
    }
}