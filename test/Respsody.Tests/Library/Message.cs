using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Respsody.Tests.Library;

[DebuggerDisplay("{ToDebugString()}")]
public readonly ref struct Message(ulong flags, ReadOnlySpan<char> payload)
{
    public int Write(Span<byte> destination)
    {
        var payloadBytes = MemoryMarshal.AsBytes(Payload);

        var payloadLength = payloadBytes.Length + sizeof(ulong);
        BinaryPrimitives.WriteInt32LittleEndian(destination, payloadLength);
        BinaryPrimitives.WriteUInt64LittleEndian(destination = destination[sizeof(int)..], Flags);
        payloadBytes.CopyTo(destination[sizeof(ulong)..]);

        return sizeof(int) + payloadLength;
    }

    public static Message Read(Span<byte> encoded)
    {
        var flags = BinaryPrimitives.ReadUInt64LittleEndian(encoded = encoded[sizeof(int)..]);

        return new Message(
            flags,
            MemoryMarshal.Cast<byte, char>(
                encoded[sizeof(ulong)..]
            )
        );
    }

    public readonly ulong Flags = flags;
    public readonly ReadOnlySpan<char> Payload = payload;

    public (ulong, string) ToTuple()
    {
        return (Flags, new string(Payload));
    }

    public string ToDebugString()
    {
        return $"{Flags} -> {new string(Payload)}";
    }
}