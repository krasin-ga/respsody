using System.Collections.Concurrent;

namespace Respsody.Memory;

public sealed class MemoryBlocks(IMemoryBlocksPool? pool = null)
{
    public static readonly MemoryBlocks Shared = new();

    private readonly ConcurrentQueue<OutgoingBuffer> _linkedQueue = new();

    private readonly IMemoryBlocksPool _pool = pool ?? new StandardMemoryBlocksPool();

    private readonly List<WeakReference<Action<int>>> _onCreateActions = [];
    private readonly List<WeakReference<Action<int>>> _onDestructActions = [];
    private readonly List<WeakReference<Action<int>>> _onResurrectActions = [];

    public object AddEventHandlers(
        Action<int> onCreate,
        Action<int> onDestruct,
        Action<int> onResurrected)
    {
        _onCreateActions.Add(new WeakReference<Action<int>>(onCreate));
        _onDestructActions.Add(new WeakReference<Action<int>>(onDestruct));
        _onResurrectActions.Add(new WeakReference<Action<int>>(onResurrected));

        return (onCreate, onDestruct, onResurrected);
    }

    public MemoryBlock Lease(int blockSize)
    {
        var (block, isNewlyCreated) = _pool.Lease(blockSize);

        block.IncLeases();
        block.HookUpBlocks(this);

        if (!isNewlyCreated)
            return block;

        OnBlockCreated(blockSize);

        return block;
    }

    public OutgoingBuffer LeaseLinked(int blockSize, bool withPrefix, int maxPrefixSize)
    {
        if (_linkedQueue.TryDequeue(out var page))
        {
            page.Init(withPrefix, maxPrefixSize, blockSize);
            return page;
        }

        page = new OutgoingBuffer(this);
        page.Init(withPrefix, maxPrefixSize, blockSize);
        return page;
    }

    public void Return(MemoryBlock memoryBlock)
    {
        _pool.Return(memoryBlock);
    }

    public void Return(OutgoingBuffer outgoingBuffer)
    {
        _linkedQueue.Enqueue(outgoingBuffer);
    }

    private void OnBlockCreated(int blockSize)
    {
        foreach (var onCreateActionRef in _onCreateActions)
            if (onCreateActionRef.TryGetTarget(out var onCreate))
                onCreate(blockSize);
    }

    internal void OnBlockDestroyed(int destroyedBlockSize)
    {
        foreach (var onCreateActionRef in _onDestructActions)
            if (onCreateActionRef.TryGetTarget(out var onDestruct))
                onDestruct(destroyedBlockSize);
    }

    internal void OnBlockResurrected(int resurrectedBlockSize)
    {
        foreach (var onCreateActionRef in _onResurrectActions)
            if (onCreateActionRef.TryGetTarget(out var onResurrect))
                onResurrect(resurrectedBlockSize);
    }
}