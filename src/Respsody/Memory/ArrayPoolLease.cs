using System.Buffers;

namespace Respsody.Memory;

internal sealed class ArrayPoolLease(ArrayPool<byte> arrayPool, byte[] lease): IDisposable
{
    public void Dispose() => arrayPool.Return(lease);
}