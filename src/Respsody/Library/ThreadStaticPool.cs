using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Respsody.Library;

public static class ThreadStaticPool<T>
    where T : new()
{
    private const int ClipCapacity = 8;

    private static readonly ConcurrentQueue<Clip> FullClips = new();
    private static readonly ConcurrentQueue<Clip> EmptyClips = new();

    [ThreadStatic]
    private static Clip? _clip;

    static ThreadStaticPool()
    {
        for (var i = 0; i < Environment.ProcessorCount; i++)
        {
            FullClips.Enqueue(new Clip(ClipCapacity, fill: true));
            EmptyClips.Enqueue(new Clip(ClipCapacity, fill: false));
        }
    }

    public static T Get()
    {
        _clip ??= GetOrCreateFullClip();

        if (_clip.TryGetItemFromClip(out var item))
            return item;

        EmptyClips.Enqueue(_clip);

        _clip = GetOrCreateFullClip();
        return _clip.GetItem();
    }

    private static Clip GetOrCreateFullClip()
    {
        return FullClips.TryDequeue(out var fullClip) ? fullClip : new Clip(ClipCapacity, fill: true);
    }

    private static Clip GetOrCreateEmptyClip()
    {
        return EmptyClips.TryDequeue(out var emptyClip) ? emptyClip : new Clip(ClipCapacity, fill: false);
    }

    public static void Return(T obj)
    {
        _clip ??= GetOrCreateEmptyClip();
        if (_clip.ReturnItem(obj))
            return;

        FullClips.Enqueue(_clip);
        _clip = GetOrCreateEmptyClip();
        _clip.ReturnItem(obj);
    }

    private class Clip
    {
        private readonly Stack<T> _stack;
        private readonly int _capacity;

        public Clip(int capacity, bool fill)
        {
            _capacity = capacity;
            _stack = new Stack<T>(capacity);
            if (!fill)
                return;

            for (var i = 0; i < capacity; i++)
                _stack.Push(new T());
        }

        public bool ReturnItem(T item)
        {
            if (_stack.Count == _capacity)
                return false;

            _stack.Push(item);
            return true;
        }

        public bool TryGetItemFromClip([NotNullWhen(true)] out T? item)
        {
            return _stack.TryPop(out item!);
        }

        public T GetItem() => _stack.Pop();
    }
}