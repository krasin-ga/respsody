using System.Buffers;

namespace Respsody.Memory;

public interface IWritableValue
{
    void WriteToBuffer(IBufferWriter<byte> destination);
}