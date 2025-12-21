using System.Buffers;
using Respsody.Memory;

namespace Respsody;

public class WritableValue<T>(T obj, WriteToBuffer<T> write): IWritableValue
{
    public void WriteToBuffer(IBufferWriter<byte> destination)
    {
        write(obj, destination);
    }
}