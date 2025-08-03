using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Respsody.Exceptions;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Resp;

namespace Respsody;


public readonly struct Value
{
    private readonly string? _string;
    private readonly Memory<byte>? _memory;
    private readonly Encoding? _stringEncoding;

    public Value(string value, Encoding? encoding = null)
    {
        _string = value;
        _stringEncoding = encoding;
    }

    public Value(Memory<byte> memory)
    {
        _memory = memory;
    }

    internal void WriteTo(OutgoingBuffer page)
    {
        if (_memory is { } memory)
        {
            var span = memory.Span;
            page.WriteBulkString(span);
            return;
        }

        if (_string is not { } str)
            throw new RespEmptyValueException();

        var encoding = _stringEncoding ?? DefaultRespEncoding.Value;

        if (page.TryWriteBulkStringInOneBlock(_string, encoding, out var byteCount, out _))
            return;

        if (encoding.CodePage == Encoding.Unicode.CodePage)
        {
            var span = MemoryMarshal.AsBytes(_string.AsSpan());
            page.WriteBulkString(span);

            return;
        }

        if (byteCount <= Constants.MaxSizeOnStack)
        {
            Span<byte> buffer = stackalloc byte[Constants.MaxSizeOnStack];
            buffer = buffer[..encoding.GetBytes(str, buffer)];
            page.WriteBulkString(buffer);
            return;
        }

        var pooledBuffer = ArrayPool<byte>.Shared.Rent(byteCount);
        pooledBuffer = pooledBuffer[..encoding.GetBytes(str, pooledBuffer)];
        page.WriteBulkString(pooledBuffer);
        ArrayPool<byte>.Shared.Return(pooledBuffer);
    }

    public static Value Unicode(string str) => new(str, Encoding.Unicode);
    public static Value Utf8(string str) => new(str, Encoding.UTF8);
    public static Value Memory(Memory<byte> memory) => new(memory);
    public static Value ByteArray(byte[] memory) => new(memory);

}