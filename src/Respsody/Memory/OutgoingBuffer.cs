using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Respsody.Library;

namespace Respsody.Memory;

[DebuggerDisplay("{ToDebugString()}")]
public sealed class OutgoingBuffer
{
    [ThreadStatic]
    private static ThreadLocalBlock? _threadLocalBlock;

    private readonly MemoryBlocks _memoryBlocks;

    private readonly SegmentListAdapter _segmentListAdapter;
    private readonly List<WrittenSegment> _segments = [];
    private readonly BufferWriterAdapter _bufferWriterAdapter;
    private int _blockSize;

    private ThreadLocalBlock _capturedBlock = null!;
    private MemoryBlock _current = null!;

    private int _currentBlockOffset;
    private int _leases;

    private int _maxPrefixSize;
    private int _prefixOffset;

    internal OutgoingBuffer(MemoryBlocks memoryBlocks)
    {
        _memoryBlocks = memoryBlocks;
        _segmentListAdapter = new SegmentListAdapter(this);
        _bufferWriterAdapter = new BufferWriterAdapter(this);
    }

    internal void Commit()
    {
        if (_leases != 1)
            return;

        AddSegment();

        //one for command, one structured socket
        _leases = 2;
        _capturedBlock.Commit();
    }

    internal void IncLeasesConcurrently()
        => Interlocked.Increment(ref _leases);

    internal void Init(bool withPrefix, int maxPrefixSize, int blockSize)
    {
        _leases = 1;
        _maxPrefixSize = maxPrefixSize;
        _blockSize = blockSize;

        if (_threadLocalBlock == null)
            _threadLocalBlock = new ThreadLocalBlock(_memoryBlocks, blockSize);
        else if (_threadLocalBlock.Remaining < _maxPrefixSize)
        {
            _threadLocalBlock.Refill(blockSize);
        }

        var block = _threadLocalBlock.GetBlock();
        block.IncLeases();
        _currentBlockOffset = block.Written;
        if (withPrefix)
        {
            _prefixOffset = _maxPrefixSize;
            block.Advance(_maxPrefixSize);
        }
        else
            _prefixOffset = 0;

        _current = block;
        _capturedBlock = _threadLocalBlock;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MemoryBlock Extend(int blockSize)
    {
        AddSegment();
        _prefixOffset = 0;
        _threadLocalBlock!.Refill(blockSize);

        _currentBlockOffset = 0;
        _current = _threadLocalBlock.GetBlock();
        _current.IncLeases();
        _capturedBlock = _threadLocalBlock;

        return _current;
    }

    private void AddSegment()
    {
        _segments.Add(new WrittenSegment(_current, _currentBlockOffset, _current.Written - _currentBlockOffset));
    }

    [ExcludeFromCodeCoverage]
    public string ToDebugString()
    {
        var sb = new StringBuilder();

        foreach (var memoryBlock in _segments)
            sb.Append(memoryBlock.MemoryBlock.ToDebugString());

        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Write(ReadOnlySpan<byte> source)
    {
        var writableSpan = _current.GetWritableSpan();
        var available = writableSpan.Length;

        var leftToWrite = source.Length;

        if (leftToWrite <= available)
        {
            source.CopyTo(writableSpan);
            _current.Advance(leftToWrite);
            return;
        }

        var current = _current;
        while (true)
        {
            source[..available].CopyTo(writableSpan);
            current.Advance(available);

            leftToWrite -= available;

            if (leftToWrite == 0)
                break;

            source = source[available..];

            current = Extend(_blockSize);
            writableSpan = current.GetWritableSpan();
            available = source.Length > writableSpan.Length
                ? writableSpan.Length
                : source.Length;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void WriteCrLf()
    {
        const byte cr = (byte)'\r';
        const byte lf = (byte)'\n';
        var span = _current.GetWritableSpan();
        if (span.Length == 1)
        {
            span[0] = cr;
            _current.Advance(1);
            Extend(_blockSize).GetWritableSpan()[0] = lf;
            _current.Advance(1);
            return;
        }

        if (span.Length == 0)
            span = Extend(_blockSize).GetWritableSpan();

        span[0] = cr;
        span[1] = lf;

        _current.Advance(2);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Write<T>(int length, in T obj, WriteToSpan<T> writeToSpan)
    {
        if (_current.Remaining >= length)
        {
            var bytesWritten = writeToSpan(obj, _current.GetWritableSpan());
            CheckWrittenBytesCount(length, bytesWritten);
            _current.Advance(bytesWritten);
            return;
        }

        if (length <= 256)
        {
            Span<byte> destination = stackalloc byte[256];

            var bytesWritten = writeToSpan(obj, destination);
            CheckWrittenBytesCount(length, bytesWritten);

            Write(destination[..bytesWritten]);
            return;
        }

        var arrayPool = ArrayPool<byte>.Shared;
        var pooled = arrayPool.Rent(length);

        var bytesWrittenToArray = writeToSpan(obj, pooled);
        CheckWrittenBytesCount(length, bytesWrittenToArray);

        Write(pooled.AsSpan()[..bytesWrittenToArray]);

        arrayPool.Return(pooled);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Write<T, TArgument>(
        int length,
        in T obj,
        TArgument argument,
        WriteToSpan<T, TArgument> writeToSpan)
    {
        if (_current.Remaining >= length)
        {
            var bytesWritten = writeToSpan(obj, argument, _current.GetWritableSpan());
            CheckWrittenBytesCount(length, bytesWritten);

            _current.Advance(bytesWritten);
            return;
        }

        if (length <= Constants.MaxSizeOnStack)
        {
            Span<byte> destination = stackalloc byte[Constants.MaxSizeOnStack];
            var bytesWritten = writeToSpan(obj, argument, destination);

            CheckWrittenBytesCount(length, bytesWritten);

            Write(destination[..bytesWritten]);
            return;
        }

        var arrayPool = ArrayPool<byte>.Shared;
        var pooled = arrayPool.Rent(length);
        var bytesWrittenToArray = writeToSpan(obj, argument, pooled);
        CheckWrittenBytesCount(length, bytesWrittenToArray);

        Write(pooled.AsSpan()[..bytesWrittenToArray]);

        arrayPool.Return(pooled);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckWrittenBytesCount(int length, int bytesWritten)
    {
        if (bytesWritten != length)
            throw new InvalidOperationException();
    }

    internal void FreeByOwner()
        => FreeInternal();

    internal void FreeByDeliveryPipeline()
        => FreeInternal();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FreeInternal()
    {
        var decrement = Interlocked.Decrement(ref _leases);
        if (decrement == 0)
        {
            Free();
            return;
        }

        if (decrement < 0)
            throw new InvalidOperationException("Buffer has been freed more times than expected");
    }

    private void Free()
    {
        var block = Exchange();
        if (block == null)
            return;

        foreach (var segment in _segments)
            segment.MemoryBlock.Dispose();

        _segments.Clear();
        _prefixOffset = 0;
        _currentBlockOffset = 0;
        _capturedBlock = null!;

        _memoryBlocks.Return(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MemoryBlock? Exchange()
    {
        return Interlocked.Exchange(ref _current, null!);
    }

    public Span<byte> CommitPrefix(int length)
    {
        ref var segment = ref CollectionsMarshal.AsSpan(_segments)[0];
        var startIndex = _prefixOffset - length;
        var actualPrefixMemory = segment.MemoryBlock.GetWrittenMemoryUnsafe().Slice(segment.Offset + startIndex, length);
        segment.Offset += startIndex;
        segment.Length -= startIndex;

        return actualPrefixMemory.Span;
    }

    public IList<ArraySegment<byte>> AsSegmentList() => _segmentListAdapter;

    internal MemoryBlock GetCurrentBlock()
        => _current;

    public bool TryCopyTo(Span<byte> span, out int bytesWritten)
    {
        if (_segments.Count == 1)
        {
            var segmentSpan = _segments[0].AsSpan();
            if (segmentSpan.Length > span.Length)
            {
                bytesWritten = 0;
                return false;
            }

            segmentSpan.CopyTo(span);
            bytesWritten = segmentSpan.Length;
            return true;
        }

        if (_segments.Count >= 8)
        {
            bytesWritten = 0;
            return false;
        }

        var total = 0;
        foreach (var writtenSegment in _segments)
            total += writtenSegment.Length;

        if (total > span.Length)
        {
            bytesWritten = 0;
            return false;
        }

        foreach (var segment in _segments)
        {
            segment.AsSpan().CopyTo(span);
            span = span[segment.Length..];
        }

        bytesWritten = total;
        return true;
    }

    private class ThreadLocalBlock(MemoryBlocks blocks, int blockSize)
    {
        private MemoryBlock _block = blocks.Lease(blockSize);
        private bool _isCommited = true;
        public int Remaining => _block.Remaining;

        public void Commit() => _isCommited = true;

        public MemoryBlock GetBlock()
        {
            if (!_isCommited)
                throw new InvalidOperationException(
                    "The command write operation was interrupted before completion. " +
                    "The thread must complete writing the current command before creating a new one.");

            _isCommited = false;

            return _block;
        }

        public void Refill(int newBlockSize)
        {
            _block.Dispose();
            _block = blocks.Lease(newBlockSize);
            _isCommited = true;
        }
    }

    private struct WrittenSegment(MemoryBlock memoryBlock, int offset, int length)
    {
        public ArraySegment<byte> AsSegment()
        {
            return MemoryBlock.AsSegment().Slice(Offset, Length);
        }

        public ReadOnlySpan<byte> AsSpan()
        {
            return MemoryBlock.GetWrittenMemory().Slice(Offset, Length).Span;
        }

        public MemoryBlock MemoryBlock { get; set; } = memoryBlock;
        public int Offset { get; set; } = offset;
        public int Length { get; set; } = length;
    }

    private class SegmentListAdapter(OutgoingBuffer p) : IList<ArraySegment<byte>>
    {
        public int Count => p._segments.Count;
        public bool IsReadOnly => throw new NotSupportedException();

        public ArraySegment<byte> this[int index]
        {
            get => p._segments[index].AsSegment();
            set => throw new NotSupportedException();
        }

        public IEnumerator<ArraySegment<byte>> GetEnumerator()
        {
            throw new NotSupportedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(ArraySegment<byte> item)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(ArraySegment<byte> item)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(ArraySegment<byte>[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        public bool Remove(ArraySegment<byte> item)
        {
            throw new NotSupportedException();
        }

        public int IndexOf(ArraySegment<byte> item)
        {
            throw new NotSupportedException();
        }

        public void Insert(int index, ArraySegment<byte> item)
        {
            throw new NotSupportedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }
    }

    public class BufferWriterAdapter(OutgoingBuffer p): IBufferWriter<byte>
    {
        public void Advance(int count)
        {
            p.GetCurrentBlock().Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            var block = p.GetCurrentBlock();
            return block.Remaining >= sizeHint
                ? block.GetWritableMemory()
                : p.Extend(sizeHint).GetWritableMemory();
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            var block = p.GetCurrentBlock();
            return block.Remaining >= sizeHint 
                ? block.GetWritableSpan() 
                : p.Extend(sizeHint).GetWritableSpan();
        }
    }
}