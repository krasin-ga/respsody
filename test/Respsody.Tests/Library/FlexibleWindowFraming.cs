using System;
using System.Collections.Generic;
using Respsody.Memory;
using Respsody.Network;

namespace Respsody.Tests.Library;

public class FlexibleWindowFraming(int windowSize, int blockSize)
    : Framing<TestSequence>(
        new WindowSize(1, windowSize),
        new MemoryBlocks(new NoopMemoryBlocksPool()),
        receivingBlockSize: blockSize)
{
    private readonly List<char> _decisions = [];
    private readonly List<char> _observed = [];

    protected override Sequence<TestSequence> Create()
    {
        return new Sequence<TestSequence>();
    }

    protected override Decision Advance(
        ref Sequence<TestSequence> sequence,
        ReadOnlySpan<byte> window)
    {
        for (ushort i = 0; i < window.Length; i++)
        {
            var @char = (char)window[i];
            _observed.Add(@char);

            if (@char == '[')
                sequence.Context.OpenCount++;

            if (@char == ']'
                && sequence.Context.OpenCount > 0
                && --sequence.Context.OpenCount == 0)
            {
                _decisions.Add('>');
                return Decision.MarkBoundary(i);
            }

            _decisions.Add('_');
        }

        return default;
    }

    public override string ToString()
    {
        return
            new string([.. _decisions])
            + Environment.NewLine
            + new string([.. _observed]);
    }
}