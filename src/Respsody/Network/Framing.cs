using System.Buffers;
using System.Runtime.CompilerServices;
using Respsody.Memory;

namespace Respsody.Network;

public abstract class Framing<T>: IDisposable
{
    private readonly ArrayPool<byte> _arrayPool;
    private readonly MemoryBlocks _memoryBlocks;
    private readonly int _receivingBlockSize;
    private readonly List<MemoryBlock> _tail = new();

    private readonly WindowSize _windowSize;
    private int _alreadyConsumed;

    private bool _awaitingMoreData;
    private ReadyFrames<T>? _cs;
    private MemoryBlock _currentBlock;
    private Sequence<T> _currentSequence;
    private bool _hasCurrentSequence;

    private int _toSkip;
    private bool _disposed;

    protected Framing(
        WindowSize windowSize,
        MemoryBlocks memoryBlocks,
        int receivingBlockSize,
        ArrayPool<byte>? arrayPool = null)
    {
        _windowSize = windowSize;
        _arrayPool = arrayPool ?? ArrayPool<byte>.Shared;
        _memoryBlocks = memoryBlocks;
        _receivingBlockSize = receivingBlockSize;
        _currentBlock = _memoryBlocks.Lease(_receivingBlockSize);
    }

    protected abstract Sequence<T> Create();

    /// <summary>
    /// Get block to consume. For single-threaded use only
    /// </summary>
    /// <returns></returns>
    public MemoryBlock GetReceivingBlock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_currentBlock.Remaining >= _windowSize.Min)
        {
            _currentBlock.IncLeases();
            return _currentBlock;
        }

        if (_hasCurrentSequence)
            AddToTail(_currentBlock);
        else if (_currentBlock.Written > _alreadyConsumed)
        {
            var next = _memoryBlocks.Lease(_receivingBlockSize);
            next.IncLeases();
            _currentBlock.CopyTo(next, _alreadyConsumed);
            _currentBlock.DecLeases();
            _currentBlock = next;
            _alreadyConsumed = 0;
            return next;
        }

        _currentBlock.DecLeases();
        _currentBlock = _memoryBlocks.Lease(_receivingBlockSize);
        _currentBlock.IncLeases();
        _alreadyConsumed = 0;
        return _currentBlock;
    }

    protected abstract Decision Advance(
        ref Sequence<T> sequence,
        ReadOnlySpan<byte> window);

    private Frame<T> MakeMemory(
        in Sequence<T> sequence,
        ArrayPool<byte> arrayPool,
        MemoryBlock current,
        List<MemoryBlock> tail,
        int position = 0)
    {
        var length = sequence.Length;

        if (tail.Count == 0)
            return new Frame<T>(
                sequence.Context,
                current.Lease(sequence.StartIndex, length),
                current);

        var rentedBytes = arrayPool.Rent(length);
        var span = rentedBytes.AsSpan(0, length);
        var remainingLength = length;

        if (tail.Count > 0)
        {
            var start = sequence.StartIndex;
            foreach (var memoryBlock in tail)
            {
                var memory = memoryBlock.GetWrittenMemory()[start..];
                memory.Span.CopyTo(span);
                var memoryLength = memory.Length;
                span = span[memoryLength..];
                start = 0;
                memoryBlock.DecLeases();
                remainingLength -= memoryLength;
            }
        }

        current.GetWrittenMemory()[position..remainingLength].Span.CopyTo(span);
        _tail.Clear();
        return new Frame<T>(
            sequence.Context,
            rentedBytes.AsMemory(0, length),
            new ArrayPoolLease(arrayPool, rentedBytes)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public ReadyFrames<T> Feed(MemoryBlock current)
    {
        var bytes = current.GetWrittenMemory().Span;
        var effectiveLength = bytes.Length - _alreadyConsumed;

        if (_toSkip > effectiveLength)
        {
            _toSkip -= effectiveLength;
            _alreadyConsumed += effectiveLength;

            return ReadyFrames<T>.Empty;
        }

        var sequences = _cs ?? new ReadyFrames<T>(this);
        _cs = null!;
        _alreadyConsumed += _toSkip;
        var i = _alreadyConsumed;
        _toSkip = 0;

        if (_awaitingMoreData)
        {
            var readySequence = MakeMemory(_currentSequence, _arrayPool, current, _tail);
            sequences.Add(readySequence);

            _awaitingMoreData = false;
            _hasCurrentSequence = false;
        }

        for (; i < bytes.Length;)
        {
            if (i + _windowSize.Min > bytes.Length)
                break;

            if (!_hasCurrentSequence)
            {
                _currentSequence = Create();
                _currentSequence.StartIndex = i;
                _hasCurrentSequence = true;
            }

            var window = _windowSize.SliceBlock(bytes, i);

            var decision = Advance(
                ref _currentSequence,
                window);

            switch (decision.SelectedMode)
            {
                case Decision.Mode.MarkBoundary:
                    var offset = decision.Offset;
                    _currentSequence.Length += offset + 1;
                    sequences.Add(
                        MakeMemory(_currentSequence, _arrayPool, current, _tail)
                    );

                    _hasCurrentSequence = false;
                    i += offset + 1;

                    _alreadyConsumed = i;

                    break;
                case Decision.Mode.None:
                    _currentSequence.Length += window.Length;
                    i += window.Length;
                    _alreadyConsumed = i;

                    break;
                case Decision.Mode.Length:

                    _currentSequence.Length += decision.Offset + decision.Length;

                    var remainingWrittenBytes = bytes.Length - i - decision.Offset;

                    if (remainingWrittenBytes >= decision.Length)
                    {
                        sequences.Add(
                            MakeMemory(
                                _currentSequence,
                                _arrayPool,
                                current,
                                _tail)
                        );

                        i += decision.Offset + decision.Length;
                        _hasCurrentSequence = false;
                        _alreadyConsumed = i;
                    }
                    else
                    {
                        _alreadyConsumed = bytes.Length;

                        var leftToReceive = decision.Length - remainingWrittenBytes;
                        i += decision.Offset + decision.Length;
                        _toSkip = leftToReceive;
                        _awaitingMoreData = true;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return sequences;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToTail(MemoryBlock current)
    {
        _tail.Add(current);
        current.IncLeases();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Return(ReadyFrames<T> readyFrames)
    {
        _cs = readyFrames;
    }

    public void Reset()
    {
        _hasCurrentSequence = false;
        _currentSequence = default;
        _awaitingMoreData = false;
        _alreadyConsumed = 0;
        _tail.Clear();
        _toSkip = 0;
        
        if (_currentBlock.Written > 0)
            _currentBlock = _memoryBlocks.Lease(_receivingBlockSize);
    }

    public virtual void Dispose()
    {
        _currentBlock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}