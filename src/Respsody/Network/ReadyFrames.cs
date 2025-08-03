using System.Diagnostics;
using Respsody.Library;
using Respsody.Memory;

namespace Respsody.Network;

public sealed class ReadyFrames<T> : IDisposable
{
    public static readonly ReadyFrames<T> Empty = new();
    private readonly Framing<T>? _parent;
    private readonly List<Frame<T>> _frames = new(16);

    public bool IsEmpty => _frames.Count == 0;

    public ReadyFrames(Framing<T> parent)
    {
        _parent = parent;
    }

    private ReadyFrames()
    {
        _parent = null!;
    }

    public List<Frame<T>>.Enumerator GetEnumerator()
        => _frames.GetEnumerator();

    public void Dispose()
    {
        if (_parent is null)
            return;

        _frames.ClearWithoutZeroing();
        _parent.Return(this);
    }

    public void Add(Frame<T> memory)
    {
        Debug.Assert(_parent is { });

        _frames.Add(memory);
    }
}