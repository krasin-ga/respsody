using System.Buffers.Binary;
using Respsody.Memory;
using Respsody.Network;

namespace Respsody.Benchmarks.Library;

public sealed class MessageFraming(int blockSize) :
    Framing<None>(new WindowSize(4), new MemoryBlocks(), blockSize)
{
    protected override Sequence<None> Create()
    {
        return new Sequence<None>();
    }

    protected override Decision Advance(
        ref Sequence<None> sequence,
        ReadOnlySpan<byte> window)
    {
        return Decision.PredefinedLength(
            BinaryPrimitives.ReadInt32LittleEndian(window) + window.Length
        );
    }
}