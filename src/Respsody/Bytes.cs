using System.Text;

namespace Respsody;

public readonly struct Bytes(Memory<byte> bytes) : IEquatable<Bytes>
{
    private readonly Memory<byte> _bytes = bytes;

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.AddBytes(_bytes.Span);
        return hashCode.ToHashCode();
    }

    public bool Equals(Bytes other)
    {
        return other._bytes.Span.SequenceEqual(_bytes.Span);
    }

    public override bool Equals(object? obj)
    {
        return obj is Bytes other && Equals(other);
    }

    public static bool operator ==(Bytes left, Bytes right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Bytes left, Bytes right)
    {
        return !(left == right);
    }
    public string ToString(Encoding encoding)
    {
        return encoding.GetString(_bytes.Span);
    }
}