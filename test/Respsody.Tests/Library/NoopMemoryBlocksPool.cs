using Respsody.Memory;

namespace Respsody.Tests.Library;

public class NoopMemoryBlocksPool : IMemoryBlocksPool
{
    public (MemoryBlock Block, bool IsNewlyCreated) Lease(int blockSize)
    {
        return (new MemoryBlock(blockSize), true);
    }

    public void Return(MemoryBlock memoryBlock)
    {
    }
}