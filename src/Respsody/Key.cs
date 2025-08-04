using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Respsody.Exceptions;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Resp;

namespace Respsody;

public readonly struct Key
{
    private readonly string? _string;
    private readonly ReadOnlyMemory<byte>? _memory;
    private readonly Encoding? _stringEncoding;
    private const ushort Zero = 0;

    public Key(string key, Encoding? encoding = null)
    {
        _string = key;
        _stringEncoding = encoding;
    }

    public Key(ReadOnlyMemory<byte> memory)
    {
        _memory = memory;
    }

    internal void WriteTo(OutgoingBuffer page)
    {
        WriteAndCalculateHashSlotInternal(page, calculate: false);
    }

    internal ushort WriteAndCalculateHashSlot(OutgoingBuffer page)
    {
        return WriteAndCalculateHashSlotInternal(page, calculate: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private ushort WriteAndCalculateHashSlotInternal(OutgoingBuffer page, bool calculate)
    {
        if (_memory is { } memory)
        {
            var span = memory.Span;
            page.WriteBulkString(span);

            return calculate
                ? CalculateHashSlot(span)
                : Zero;
        }

        if (_string is not { } str)
            throw new RespEmptyKeyException();

        var encoding = _stringEncoding ?? DefaultRespEncoding.Value;

        if (page.TryWriteBulkStringInOneBlock(_string, encoding, out var byteCount, out var written))
        {
            return calculate
                ? CalculateHashSlot(written.Span)
                : Zero;
        }

        if (encoding.CodePage == Encoding.Unicode.CodePage)
        {
            var span = MemoryMarshal.AsBytes(_string.AsSpan());
            page.WriteBulkString(span);

            return calculate
                ? CalculateHashSlot(span)
                : Zero;
        }

        if (byteCount <= Constants.MaxSizeOnStack)
        {
            Span<byte> buffer = stackalloc byte[Constants.MaxSizeOnStack];
            buffer = buffer[..encoding.GetBytes(str, buffer)];
            page.WriteBulkString(buffer);

            return calculate
                ? CalculateHashSlot(buffer)
                : Zero;
        }

        var pooledBuffer = ArrayPool<byte>.Shared.Rent(byteCount);
        pooledBuffer = pooledBuffer[..encoding.GetBytes(str, pooledBuffer)];
        page.WriteBulkString(pooledBuffer);
        var slot = calculate
            ? CalculateHashSlot(pooledBuffer)
            : Zero;
        ArrayPool<byte>.Shared.Return(pooledBuffer);

        return slot;
    }

    public ushort CalculateHashSlot()
    {
        if (_memory is { } memory)
            return CalculateHashSlot(memory.Span);

        if (_string is not { })
            throw new RespEmptyKeyException();

        var encoding = _stringEncoding ?? DefaultRespEncoding.Value;

        if (encoding.CodePage == Encoding.Unicode.CodePage)
        {
            var span = MemoryMarshal.AsBytes(_string.AsSpan());
            return CalculateHashSlot(span);
        }

        var byteCount = encoding.GetByteCount(_string);

        if (byteCount <= Constants.MaxSizeOnStack)
        {
            Span<byte> buffer = stackalloc byte[Constants.MaxSizeOnStack];
            buffer = buffer[..encoding.GetBytes(_string, buffer)];
            return CalculateHashSlot(buffer);
        }

        var pooledBuffer = ArrayPool<byte>.Shared.Rent(byteCount);
        pooledBuffer = pooledBuffer[..encoding.GetBytes(_string, pooledBuffer)];
        var slot = CalculateHashSlot(pooledBuffer);
        ArrayPool<byte>.Shared.Return(pooledBuffer);

        return slot;
    }

    private static ushort CalculateHashSlot(ReadOnlySpan<byte> key)
    {
        const int numberOfClusterSlotsMinusOne = 16383;

        if (key.Length < 3)
            return (ushort)(CRC16.Calculate(key) & numberOfClusterSlotsMinusOne);

        var startIndex = key.IndexOf((byte)'{');
        if (startIndex == -1 || startIndex == key.Length - 1)
            return (ushort)(CRC16.Calculate(key) & numberOfClusterSlotsMinusOne);

        var relativeEndIndex = key[(startIndex + 1)..].IndexOf((byte)'}');
        if (relativeEndIndex == -1)
            return (ushort)(CRC16.Calculate(key) & numberOfClusterSlotsMinusOne);

        var endIndex = startIndex + 1 + relativeEndIndex;
        if (endIndex == startIndex + 1)
            return (ushort)(CRC16.Calculate(key) & numberOfClusterSlotsMinusOne);

        return (ushort)(CRC16.Calculate(key.Slice(startIndex + 1, endIndex - startIndex - 1))
                        & numberOfClusterSlotsMinusOne);
    }

    public static Key Unicode(string str) => new(str, Encoding.Unicode);
    public static Key Utf8(string str) => new(str, Encoding.UTF8);
}