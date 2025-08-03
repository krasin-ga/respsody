using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Respsody.Memory;

/// <summary>
/// Continuous region of memory.
/// Writes are not thread safe.
/// Consuming of leased regions can be performed by multiple threads.
/// </summary>
[DebuggerDisplay("{ToDebugString()}")]
public sealed class MemoryBlock : IDisposable
{
    private readonly byte[] _buffer;
    private readonly int _bufferSize;

    private int _leases;
    private MemoryBlocks _memoryBlocks = null!;
    private int _offset;
    private int _position;

    public int Remaining => _bufferSize - _position;
    public int Written => _position;
    public int Size => _bufferSize;

    public MemoryBlock(int bufferSize)
    {
        _bufferSize = bufferSize;
        _buffer = GC.AllocateArray<byte>(bufferSize, pinned: false);
    }

    public MemoryBlock(MemoryBlocks blocks, int bufferSize)
        : this(bufferSize)
    {
        HookUpBlocks(blocks);
    }

    public MemoryBlock(byte[] buffer)
    {
        _buffer = buffer;
        _bufferSize = buffer.Length;
    }

    public byte[] GetUnderlyingBufferUnsafe() => _buffer;

    ~MemoryBlock()
    {
        if (_leases == 1)
        {
            DecLeases();
            GC.ReRegisterForFinalize(this);
            _memoryBlocks.OnBlockResurrected(_bufferSize);
            return;
        }

        _memoryBlocks.OnBlockDestroyed(_bufferSize);
    }

    internal void HookUpBlocks(MemoryBlocks memoryBlocks)
        => _memoryBlocks = memoryBlocks;

    public ArraySegment<byte> AsSegment() =>
        new(_buffer, _offset, _position - _offset);

    public void ReturnLease()
    {
        var decrement = Interlocked.Decrement(ref _leases);
        if (decrement < 0)
            throw new InvalidOperationException("The lease has been returned more times than it has been taken. Check for multiple disposals");

        if (decrement == 0)
        {
            Reset();
            _memoryBlocks.Return(this);
        }
    }

    [ExcludeFromCodeCoverage]
    public string ToDebugString()
    {
        return $"({_bufferSize / (double)Written:P1})" + Encoding.UTF8.GetString(GetWrittenMemory().Span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        _position += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckWrittenBytesCount(int length, int bytesWritten)
    {
        if (bytesWritten != length)
            throw new InvalidOperationException();
    }

    private void Reset()
    {
        _position = 0;
        _offset = 0;
    }

    public ReadOnlyMemory<byte> GetWrittenMemory()
    {
        return _buffer.AsMemory(_offset, _position - _offset);
    }

    public Memory<byte> GetWrittenMemoryUnsafe()
    {
        return _buffer.AsMemory(_offset, _position - _offset);
    }

    public Memory<byte> GetWritableMemory()
    {
        return _buffer.AsMemory(_position);
    }

    public Span<byte> GetWritableSpan()
        => _buffer.AsSpan(_position);

    internal void CopyTo(MemoryBlock target, int fromPosition)
    {
        var slice = GetWrittenMemory().Span[fromPosition..];
        slice.CopyTo(target.GetWritableSpan());

        target.Advance(slice.Length);
    }

    public Memory<byte> Lease(int start, int length)
    {
        IncLeases();
        return _buffer.AsMemory(start, length);
    }

    internal void IncLeases()
    {
        Interlocked.Increment(ref _leases);
    }

    internal void DecLeases()
    {
        ReturnLease();
    }

    public void Dispose()
    {
        ReturnLease();
    }
}