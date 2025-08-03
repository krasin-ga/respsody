using System;
using System.Collections.Generic;
using Respsody.Memory;
using Respsody.Network;

namespace Respsody.Tests.Library;

public class SingleByteFraming(int blockSize)
    : Framing<TestSequence>(windowSize: new WindowSize(1), new MemoryBlocks(new NoopMemoryBlocksPool()), blockSize)
{
    private readonly List<char> _decisions = new();
    private readonly List<char> _seen = new();

    protected override Sequence<TestSequence> Create()
    {
        return new Sequence<TestSequence>();
    }

    protected override Decision Advance(
        ref Sequence<TestSequence> sequence,
        ReadOnlySpan<byte> window)
    {
        var @char = (char)window[0];
        _seen.Add(@char);

        switch (@char)
        {
            case '[':
                sequence.Context.OpenCount++;
                break;
            case ']' when sequence.Context.OpenCount > 0 && --sequence.Context.OpenCount == 0:
                _decisions.Add('>');
                return Decision.MarkBoundary(0);
        }

        _decisions.Add('_');
        return default;
    }

    public override string ToString()
    {
        return
            new string(_decisions.ToArray())
            + Environment.NewLine
            + new string(_seen.ToArray());
    }
}