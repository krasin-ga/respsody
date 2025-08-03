using BenchmarkDotNet.Attributes;
using Respsody.Benchmarks.Library;

namespace Respsody.Benchmarks;

[MemoryDiagnoser, ThreadingDiagnoser, OperationsPerSecond, InProcess]
public class FramingBenchmark
{
    private MessageFraming _testFraming = null!;

    [GlobalSetup]
    public void Setup()
    {
        _testFraming = new MessageFraming(blockSize: 4096);
    }

    [Benchmark]
    public ulong OneMessage()
    {
        var msg1 = new Message(2, "hello".AsSpan());

        using var block = _testFraming.GetReceivingBlock();
        block.Advance(msg1.Write(block.GetWritableSpan()));

        ulong flags = 0;
        using var sequenceMemories = _testFraming.Feed(block);
        foreach (var sequenceMemory in sequenceMemories)
            using (sequenceMemory)
                flags = Message.Read(sequenceMemory.Span).Flags;

        if (flags != 2)
            throw new InvalidOperationException();

        return flags;
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public ulong FiveMessages()
    {
        var msg1 = new Message(2, "hello".AsSpan());
        var msg2 = new Message(4, ", ".AsSpan());
        var msg3 = new Message(5, "world".AsSpan());
        var msg4 = new Message(1, "!".AsSpan());
        var msg5 = new Message(42, "".AsSpan());

        using var block = _testFraming.GetReceivingBlock();

        block.Advance(msg1.Write(block.GetWritableSpan()));
        block.Advance(msg2.Write(block.GetWritableSpan()));
        block.Advance(msg3.Write(block.GetWritableSpan()));
        block.Advance(msg4.Write(block.GetWritableSpan()));
        block.Advance(msg5.Write(block.GetWritableSpan()));

        ulong lastMessageFlags = 0;
        using var sequenceMemories = _testFraming.Feed(block);
        foreach (var sequenceMemory in sequenceMemories)
            using (sequenceMemory)
                lastMessageFlags = Message.Read(sequenceMemory.Span).Flags;

        if (lastMessageFlags != 42)
            throw new InvalidOperationException();

        return lastMessageFlags;
    }
}