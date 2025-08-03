namespace Respsody.Memory;

internal class NoOpPool : IMemoryBlocksPool
{
    public (MemoryBlock Block, bool IsNewlyCreated) Lease(int blockSize)
    {
        return (new MemoryBlock(blockSize), true);
    }

    public void Return(MemoryBlock memoryBlock)
    {
    }
}