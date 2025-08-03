using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text;

namespace Respsody.Memory;

[DebuggerDisplay("{ToDebugString()}")]
public readonly struct Frame<T>(T context, Memory<byte> memory, IDisposable lifetime)
    : IDisposable
{
    public T Context { get; } = context;
    public Memory<byte> Memory { get; } = memory;
    public Span<byte> Span => Memory.Span;

    public void Dispose()
    {
        lifetime.Dispose();
    }

    public string ToDebugString()
    {
        return Encoding.UTF8.GetString(Memory.Span);
    }

    [Pure]
    public (Frame<T> Frame, IDisposable Lifetime) AsExternallyOwned()
    {
        return (new Frame<T>(Context, Memory, EmptyDisposable.Instance), lifetime);
    }
}