namespace Respsody.Memory;

public interface IMemoryBlocksPool
{
    (MemoryBlock Block, bool IsNewlyCreated) Lease(int blockSize);
    void Return(MemoryBlock memoryBlock);
}