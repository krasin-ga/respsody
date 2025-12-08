using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Respsody.Memory;

public class StandardMemoryBlocksPool(long memoryLimitInBytes = 512 * 1024 * 1024) : IMemoryBlocksPool
{
    private readonly InnerPool[] _pools = [.. A000079.Select(_ => new InnerPool())];

    private static int[] A000079 =>
    [
        0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144, 524288,
        1048576,
        2097152, 4194304, 8388608, 16777216, 33554432, 67108864, 134217728, 268435456, 536870912
    ];

    private long _accumulatedBytes;

    public (MemoryBlock Block, bool IsNewlyCreated) Lease(int blockSize)
    {
        var pool = GetPool(blockSize);
        if (pool.TryPop(out var block))
        {
            Interlocked.Add(ref _accumulatedBytes, -block.Size);
            return (block, IsNewlyCreated: false);
        }

        block = new MemoryBlock(blockSize);

        return (block, IsNewlyCreated: true);
    }

    private InnerPool GetPool(int blockSize)
    {
        const int minBlockSizeIdx = 7; // 64 bytes
        var index = BitOperations.RoundUpToPowerOf2((uint)blockSize) switch
        {
            0 => minBlockSizeIdx,
            1 => minBlockSizeIdx,
            2 => minBlockSizeIdx,
            4 => minBlockSizeIdx,
            8 => minBlockSizeIdx,
            16 => minBlockSizeIdx,
            32 => minBlockSizeIdx,
            64 => minBlockSizeIdx,
            128 => 8,
            256 => 9,
            512 => 10,
            1024 => 11,
            2048 => 12,
            4096 => 13,
            8192 => 14,
            16384 => 15,
            32768 => 16,
            65536 => 17,
            131072 => 18,
            262144 => 19,
            524288 => 20,
            1048576 => 21,
            2097152 => 22,
            4194304 => 23,
            8388608 => 24,
            16777216 => 25,
            33554432 => 26,
            67108864 => 27,
            134217728 => 28,
            268435456 => 29,
            536870912 => 30,
            _ => throw new ArgumentOutOfRangeException(nameof(blockSize), "block size is too large")
        };
        return _pools[index];
    }

    public void Return(MemoryBlock memoryBlock)
    {
        if (Interlocked.Add(ref _accumulatedBytes, memoryBlock.Size) >= memoryLimitInBytes)
        {
            Interlocked.Add(ref _accumulatedBytes, -_accumulatedBytes);
            return;
        }

        GetPool(memoryBlock.Size).Push(memoryBlock);
    }

    private class InnerPool()
    {
        private readonly ConcurrentStack<MemoryBlock> _stack = [];

        public bool TryPop([NotNullWhen(true)] out MemoryBlock? o)
        {
            return _stack.TryPop(out o);
        }

        public void Push(MemoryBlock memoryBlock)
        {
            _stack.Push(memoryBlock);
        }
    }
}