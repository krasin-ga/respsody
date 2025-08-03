using System;
using System.Buffers.Binary;
using Respsody.Memory;
using Respsody.Network;

namespace Respsody.Tests.Library;

public sealed class LengthPrefixedFraming(int blockSize) : Framing<None>(new WindowSize(4), new MemoryBlocks(new NoopMemoryBlocksPool()), blockSize)
{
    protected override Sequence<None> Create() => new();

    protected override Decision Advance(
        ref Sequence<None> sequence,
        ReadOnlySpan<byte> window)
    {
        return Decision.PredefinedLength(
            BinaryPrimitives.ReadInt32LittleEndian(window) + window.Length
        );
    }
}