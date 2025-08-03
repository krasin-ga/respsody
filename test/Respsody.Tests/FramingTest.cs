using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Respsody.Memory;
using Respsody.Tests.Library;
using Xunit;
using Xunit.Abstractions;

namespace Respsody.Tests;

public class FramingTest(ITestOutputHelper testOutputHelper)
{
    [Theory]
    [InlineData("[[ab]][cd][[ef]]", "['[[ab]]', '[cd]', '[[ef]]']")]
    [InlineData("[ab][cd][ef]", "['[ab]', '[cd]', '[ef]']")]
    [InlineData("[abc][def]", "['[abc]', '[def]']")]
    [InlineData("[a][b]", "['[a]', '[b]']")]
    public void SingleByteWindow(string input, string expected)
    {
        var expectations = JsonSerializer.Deserialize<string[]>(expected.Replace("'", "\""));

        for (var blockSize = 1; blockSize <= 15; blockSize++)
        {
            var actual = new List<string>();

            var testControlSequence = new SingleByteFraming(blockSize);

            var bytes = Encoding.ASCII.GetBytes(input).AsSpan();
            var leftToWrite = bytes.Length;

            while (leftToWrite > 0)
            {
                var memoryBlock = testControlSequence.GetReceivingBlock();
                var memory = memoryBlock.GetWritableMemory();
                var toWrite = Math.Min(memory.Length, bytes.Length);

                bytes[..toWrite].CopyTo(memory.Span);
                memoryBlock.Advance(toWrite);
                var sequences = testControlSequence.Feed(memoryBlock);

                foreach (var sequence in sequences)
                {
                    actual.Add(Encoding.ASCII.GetString(sequence.Memory.Span));
                }

                leftToWrite -= toWrite;
                bytes = bytes[toWrite..];
            }

            testOutputHelper.WriteLine($"{testControlSequence}");
            testOutputHelper.WriteLine($"{blockSize}:{string.Join(", ", actual)}");

            Assert.Equal(expected: expectations, actual: actual);
            testOutputHelper.WriteLine($"Passed!");
            testOutputHelper.WriteLine($"");
        }
    }

    [Theory]
    [InlineData("[ab][cd][ef]", "['[ab]', '[cd]', '[ef]']")]
    [InlineData("[abc][def]", "['[abc]', '[def]']")]
    [InlineData("[a][b]", "['[a]', '[b]']")]
    public void FlexibleWindow(string input, string expected)
    {
        var expectations = JsonSerializer.Deserialize<string[]>(expected.Replace("'", "\""));

        for (var blockSize = 1; blockSize <= 15; blockSize++)
        {
            for (int windowSize = 1; windowSize <= blockSize; windowSize++)
            {
                testOutputHelper.WriteLine($"Testing {blockSize}:{windowSize}");
                var actual = new List<string>();

                var testControlSequence = new FlexibleWindowFraming(windowSize, blockSize);

                var bytes = Encoding.ASCII.GetBytes(input).AsSpan();
                var leftToWrite = bytes.Length;

                while (leftToWrite > 0)
                {
                    var memoryBlock = testControlSequence.GetReceivingBlock();
                    var memory = memoryBlock.GetWritableMemory();
                    var toWrite = Math.Min(memory.Length, bytes.Length);

                    bytes[..toWrite].CopyTo(memory.Span);
                    memoryBlock.Advance(toWrite);
                    var sequences = testControlSequence.Feed(memoryBlock);
                    foreach (var sequence in sequences)
                    {
                        actual.Add(Encoding.ASCII.GetString(sequence.Memory.Span));
                    }

                    leftToWrite -= toWrite;
                    bytes = bytes[toWrite..];
                }

                testOutputHelper.WriteLine($"{testControlSequence}");
                testOutputHelper.WriteLine($"b{blockSize}|w{windowSize}:{string.Join(", ", actual)}");

                Assert.Equal(expected: expectations, actual: actual);
                testOutputHelper.WriteLine($"Passed!");
                testOutputHelper.WriteLine($"");
            }
        }
    }

    [Fact]
    public void FramingWindow()
    {
        var random = new Random(42);
        Span<char> body = stackalloc char[100_000];
        Span<byte> buffer = stackalloc byte[100_000 * 2];

        for (var blockSize = 4; blockSize <= 15; blockSize++)
        {
            var testControlSequence = new LengthPrefixedFraming(blockSize);

            for (int i = 0; i < 1000; i++)
            {

                var slice = body[..i];
                for (var j = 0; j < slice.Length; j++)
                    slice[j] = (char)random.Next(0, 256);

                var flags = (ulong)i;
                var msg = new Message(flags, slice);

                var bytes = buffer[..msg.Write(buffer)];
                var leftToWrite = bytes.Length;

                while (leftToWrite > 0)
                {
                    var memoryBlock = testControlSequence.GetReceivingBlock();
                    var memory = memoryBlock.GetWritableMemory();
                    var toWrite = Math.Min(memory.Length, bytes.Length);

                    bytes[..toWrite].CopyTo(memory.Span);
                    memoryBlock.Advance(toWrite);
                    using var sequences = testControlSequence.Feed(memoryBlock);

                    foreach (var sequence in sequences)
                    {
                        using (sequence)
                        {
                            var message = Message.Read(sequence.Memory.Span);

                            Assert.Equal(msg.Flags, message.Flags);
                            var source = new string(msg.Payload);
                            var target = new string(message.Payload);

                            Assert.Equal(source, target);
                        }
                    }

                    leftToWrite -= toWrite;
                    bytes = bytes[toWrite..];
                }
            }

        }
    }

    [Fact]
    public void FramingWindow_MultipleMessagesInSingleBlock()
    {
        var msg1 = new Message(2, "hello".AsSpan());
        var msg2 = new Message(4, ", ".AsSpan());
        var msg3 = new Message(5, "world".AsSpan());
        var msg4 = new Message(1, "!".AsSpan());
        var msg5 = new Message(42, "".AsSpan());

        var expected = new List<(ulong, string)>
        {
            msg1.ToTuple(),
            msg2.ToTuple(),
            msg3.ToTuple(),
            msg4.ToTuple(),
            msg5.ToTuple()
        };

        var testControlSequence = new LengthPrefixedFraming(blockSize: 1024);

        var block = testControlSequence.GetReceivingBlock();
            
        block.Advance(msg1.Write(block.GetWritableSpan()));
        block.Advance(msg2.Write(block.GetWritableSpan()));
        block.Advance(msg3.Write(block.GetWritableSpan()));
        block.Advance(msg4.Write(block.GetWritableSpan()));
        block.Advance(msg5.Write(block.GetWritableSpan()));

        var actual = new List<(ulong, string)>();
        foreach (var sequenceMemory in testControlSequence.Feed(block))
        {
            using (sequenceMemory)
            {
                var msg = Message.Read(sequenceMemory.Span);
                actual.Add(msg.ToTuple());
            }

        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FramingWindow_LessThanWindowReminders()
    {
        var msg1 = new Message(2, "hello".AsSpan());
        var msg2 = new Message(4, ", ".AsSpan());
        var msg3 = new Message(5, "world".AsSpan());
        var msg4 = new Message(1, "!".AsSpan());
        var msg5 = new Message(42, "".AsSpan());

        var expected = new List<(ulong, string)>
        {
            msg1.ToTuple(),
            msg2.ToTuple(),
            msg3.ToTuple(),
            msg4.ToTuple(),
            msg5.ToTuple()
        };
        var actual = new List<(ulong, string)>();

        var testControlSequence = new LengthPrefixedFraming(blockSize: 1024);

        var block = new MemoryBlock(new MemoryBlocks(), 1024);

        block.Advance(msg1.Write(block.GetWritableSpan()));
        block.Advance(msg2.Write(block.GetWritableSpan()));
        block.Advance(msg3.Write(block.GetWritableSpan()));
        block.Advance(msg4.Write(block.GetWritableSpan()));
        block.Advance(msg5.Write(block.GetWritableSpan()));

        var b1 = testControlSequence.GetReceivingBlock();
        var writtenMemory = block.GetWrittenMemory();

        var firstSlice = writtenMemory[..2];
        firstSlice.CopyTo(b1.GetWritableMemory());
        b1.Advance(firstSlice.Length);

        foreach (var sequenceMemory in testControlSequence.Feed(b1))
            using (sequenceMemory)
            {
                var msg = Message.Read(sequenceMemory.Span);
                actual.Add(msg.ToTuple());
            }

        var b2 = testControlSequence.GetReceivingBlock();
        var secondSlice = writtenMemory.Slice(2, writtenMemory.Length / 2);
        secondSlice.CopyTo(b2.GetWritableMemory());
        b2.Advance(secondSlice.Length);

        foreach (var sequenceMemory in testControlSequence.Feed(b2))
            using (sequenceMemory)
            {
                var msg = Message.Read(sequenceMemory.Span);
                actual.Add(msg.ToTuple());
            }

        var b3 = testControlSequence.GetReceivingBlock();
        var thirdSlice = writtenMemory[(2 + writtenMemory.Length / 2)..];
        thirdSlice.CopyTo(b3.GetWritableMemory());
        b3.Advance(thirdSlice.Length);

        foreach (var sequenceMemory in testControlSequence.Feed(b3))
            using (sequenceMemory)
            {
                var msg = Message.Read(sequenceMemory.Span);
                actual.Add(msg.ToTuple());
            }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FramingWindow_LessThanWindowRemindersInBlock()
    {
        var msg1 = new Message(2, "hello".AsSpan());
        var msg2 = new Message(4, ", ".AsSpan());
        var msg3 = new Message(1, "respsody! ".AsSpan());

        var expected = new List<(ulong, string)>
        {
            msg1.ToTuple(),
            msg2.ToTuple(),
            msg3.ToTuple(),
        };
        var actual = new List<(ulong, string)>();

        var testControlSequence = new LengthPrefixedFraming(blockSize: 24);

        var block = new MemoryBlock(new MemoryBlocks(), 1024);
        block.Advance(msg1.Write(block.GetWritableSpan()));
        block.Advance(msg2.Write(block.GetWritableSpan()));
        block.Advance(msg3.Write(block.GetWritableSpan()));

        var b1 = testControlSequence.GetReceivingBlock();
        var writtenMemory = block.GetWrittenMemory();

        var firstSlice = writtenMemory[..24];
        firstSlice.CopyTo(b1.GetWritableMemory());
        b1.Advance(firstSlice.Length);

        foreach (var sequenceMemory in testControlSequence.Feed(b1))
            using (sequenceMemory)
            {
                var msg = Message.Read(sequenceMemory.Span);
                actual.Add(msg.ToTuple());
            }

        var b2 = testControlSequence.GetReceivingBlock();
        var secondSlice = writtenMemory[24..30];
        secondSlice.CopyTo(b2.GetWritableMemory());
        b2.Advance(secondSlice.Length);

        foreach (var sequenceMemory in testControlSequence.Feed(b2))
            using (sequenceMemory)
            {
                var msg = Message.Read(sequenceMemory.Span);
                actual.Add(msg.ToTuple());
            }

        var b3 = testControlSequence.GetReceivingBlock();
        var thirdSlice = writtenMemory[30..46];
        thirdSlice.CopyTo(b3.GetWritableMemory());
        b3.Advance(thirdSlice.Length);

        foreach (var sequenceMemory in testControlSequence.Feed(b3))
            using (sequenceMemory)
            {
                var msg = Message.Read(sequenceMemory.Span);
                actual.Add(msg.ToTuple());
            }

        var b4 = testControlSequence.GetReceivingBlock();
        var fourthSlice = writtenMemory[46..];
        fourthSlice.CopyTo(b4.GetWritableMemory());
        b4.Advance(fourthSlice.Length);

        foreach (var sequenceMemory in testControlSequence.Feed(b4))
            using (sequenceMemory)
            {
                var msg = Message.Read(sequenceMemory.Span);
                actual.Add(msg.ToTuple());
            }

        Assert.Equal(expected, actual);
    }
}