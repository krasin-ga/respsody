using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Respsody.Library;
using Respsody.Memory;

namespace Respsody.Resp;

public static class ProtocolWriter
{
    private const int MaxPrefixBytes = 11 + 1 + 2;

    public static byte[] ConvertToBulkString(string str)
    {
        var byteCount = Encoding.UTF8.GetByteCount(str);
        return Encoding.UTF8.GetBytes($"${byteCount}\r\n{str}\r\n");
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryWriteBulkStringInOneBlock(
        this OutgoingBuffer outgoingBuffer,
        string str,
        Encoding encoding,
        out int byteCount,
        out ReadOnlyMemory<byte> written)
    {
        byteCount = encoding.GetByteCount(str);
        var currentBlock = outgoingBuffer.GetCurrentBlock();

        if (currentBlock.Remaining < byteCount + MaxPrefixBytes + 2)
        {
            written = default;
            return false;
        }

        outgoingBuffer.WriteBulkStringPrefix(byteCount);
        var memory = currentBlock.GetWritableMemory();
        encoding.GetBytes(str, memory.Span);

        WriteCrlf(memory.Span, byteCount);
        currentBlock.Advance(byteCount + 2);
        written = memory[..byteCount];

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void WriteBulkString(
        this OutgoingBuffer outgoingBuffer,
        ReadOnlySpan<byte> bytes)
    {
        var bytesLength = bytes.Length;

        outgoingBuffer.WriteBulkStringPrefix(bytesLength);
        var currentBlock = outgoingBuffer.GetCurrentBlock();
        if (currentBlock.Remaining >= bytesLength + 2)
        {
            var span = currentBlock.GetWritableSpan();
            bytes.CopyTo(span);
            WriteCrlf(span, bytesLength);
            currentBlock.Advance(bytesLength + 2);
            return;
        }

        outgoingBuffer.Write(bytes);
        outgoingBuffer.WriteCrLf();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void WriteBulkString<T>(
        this OutgoingBuffer outgoingBuffer,
        int lengthInBytes,
        in T obj,
        WriteToSpan<T> writeToSpan)
    {
        outgoingBuffer.WriteBulkStringPrefix(lengthInBytes);
        outgoingBuffer.Write(lengthInBytes, obj, writeToSpan);
        outgoingBuffer.Write(Constants.CRLF);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void WriteBulkString<T, TArg>(
        this OutgoingBuffer outgoingBuffer,
        int lengthInBytes,
        in T obj,
        TArg arg,
        WriteToSpan<T, TArg> writeToSpan)
    {
        outgoingBuffer.WriteBulkStringPrefix(lengthInBytes);
        outgoingBuffer.Write(lengthInBytes, obj, arg, writeToSpan);
        outgoingBuffer.WriteCrLf();
    }

    //$4\r\n....\r\n
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void WriteBulkStringPrefix(
        this OutgoingBuffer outgoingBuffer,
        int length)
    {
        var memoryBlock = outgoingBuffer.GetCurrentBlock();
        if (memoryBlock.Remaining >= MaxPrefixBytes)
        {
            var writableSpan = memoryBlock.GetWritableSpan();
            writableSpan[0] = (byte)RespType.BulkString;

            if (!Utf8Formatter.TryFormat(length, writableSpan[1..], out var bytesWrittenToBlock))
                throw new InvalidOperationException();

            WriteCrlf(writableSpan, bytesWrittenToBlock + 1);

            memoryBlock.Advance(bytesWrittenToBlock + 1 + 2);
            return;
        }

        Span<byte> bytes = stackalloc byte[MaxPrefixBytes];
        bytes[0] = (byte)RespType.BulkString;

        if (!Utf8Formatter.TryFormat(length, bytes[1..], out var bytesWritten))
            throw new InvalidOperationException();

        WriteCrlf(bytes, bytesWritten + 1);

        outgoingBuffer.Write(bytes[..(bytesWritten + 1 + 2)]);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void WriteBulkString(
        this OutgoingBuffer outgoingBuffer,
        int value)
    {
        const int maxBytesForInt = 11;

        Span<byte> bytes = stackalloc byte[maxBytesForInt];
        if (!Utf8Formatter.TryFormat(value, bytes, out var bytesWritten))
            throw new InvalidOperationException();

        outgoingBuffer.WriteBulkStringPrefix(bytesWritten);
        outgoingBuffer.Write(bytes[..bytesWritten]);
        outgoingBuffer.Write(Constants.CRLF);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void WriteBulkString(
        this OutgoingBuffer outgoingBuffer,
        long number)
    {
        const int maxBytesForLong = 20;

        Span<byte> bytes = stackalloc byte[maxBytesForLong];
        if (!Utf8Formatter.TryFormat(number, bytes, out var bytesWritten))
            throw new InvalidOperationException();

        outgoingBuffer.WriteBulkStringPrefix(bytesWritten);
        outgoingBuffer.Write(bytes[..bytesWritten]);
        outgoingBuffer.Write(Constants.CRLF);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void WriteArraySizePrefix(
        this OutgoingBuffer outgoingBuffer,
        int numberOfElements)
    {
        const int maxPrefixBytes = 10 + 3;

        Span<byte> bytes = stackalloc byte[maxPrefixBytes];
        bytes[0] = (byte)RespType.Array;

        if (!Utf8Formatter.TryFormat(numberOfElements, bytes[1..], out var bytesWritten))
            throw new InvalidOperationException();

        Constants.CRLF.CopyTo(bytes[(1 + bytesWritten)..]);

        var actualPrefixLength = bytesWritten + 3;

        var prefix = outgoingBuffer.CommitPrefix(actualPrefixLength);
        bytes[..actualPrefixLength].CopyTo(prefix);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void WriteArraySize(
        this OutgoingBuffer outgoingBuffer,
        int numberOfElements)
    {
        const int maxPrefixBytes = 10 + 3;

        Span<byte> bytes = stackalloc byte[maxPrefixBytes];
        bytes[0] = (byte)RespType.Array;

        if (!Utf8Formatter.TryFormat(numberOfElements, bytes[1..], out var bytesWritten))
            throw new InvalidOperationException();

        Constants.CRLF.CopyTo(bytes[(1 + bytesWritten)..]);

        var actualPrefixLength = bytesWritten + 3;

        outgoingBuffer.Write(bytes[..actualPrefixLength]);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void WriteBulkString(
        this OutgoingBuffer outgoingBuffer,
        double value)
    {
        if (double.IsNaN(value))
        {
            var nanBytes = RespConstants.Double.NaN;
            outgoingBuffer.WriteBulkStringPrefix(nanBytes.Length);
            outgoingBuffer.Write(nanBytes);
            outgoingBuffer.Write(Constants.CRLF);
            return;
        }

        if (double.IsPositiveInfinity(value))
        {
            var positiveInfinityBytes = RespConstants.Double.PositiveInfinity;
            outgoingBuffer.WriteBulkStringPrefix(positiveInfinityBytes.Length);
            outgoingBuffer.Write(positiveInfinityBytes);
            outgoingBuffer.Write(Constants.CRLF);
            return;
        }

        if (double.IsNegativeInfinity(value))
        {
            var negativeInfinityBytes = RespConstants.Double.NegativeInfinity;
            outgoingBuffer.WriteBulkStringPrefix(negativeInfinityBytes.Length);
            outgoingBuffer.Write(negativeInfinityBytes);
            outgoingBuffer.Write(Constants.CRLF);
            return;
        }

        Span<byte> intermediate = stackalloc byte[128];
        if (Utf8Formatter.TryFormat(value, intermediate, out var bytesWritten))
        {
            outgoingBuffer.WriteBulkStringPrefix(bytesWritten);
            outgoingBuffer.Write(intermediate[..bytesWritten]);
            outgoingBuffer.Write(Constants.CRLF);
            return;
        }

        var str = value.ToString("G17", NumberFormatInfo.InvariantInfo);
        Span<byte> bytes = stackalloc byte[str.Length];

        if (bytes.Length != Encoding.UTF8.GetBytes(str, bytes))
            throw new InvalidOperationException();

        outgoingBuffer.WriteBulkStringPrefix(bytes.Length);
        outgoingBuffer.Write(bytes);
        outgoingBuffer.Write(Constants.CRLF);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteCrlf(Span<byte> span, int offset)
    {
        const byte cr = (byte)'\r';
        const byte lf = (byte)'\n';

        span[offset] = cr;
        span[offset + 1] = lf;
    }
}