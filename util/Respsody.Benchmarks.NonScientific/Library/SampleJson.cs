using Respsody.Memory;
using System.Buffers;
using System.Text.Json;

namespace Respsody.Benchmarks.NonScientific.Library;

public sealed class SampleJson: IWritableValue
{
    public required string StringValue { get; set; }
    public long ScalarValue { get; set; }
    public required float[] VectorValue { get; set; }
    public void WriteToBuffer(IBufferWriter<byte> destination)
    {
        var utf8JsonWriter = new Utf8JsonWriter(destination);
        JsonSerializer.Serialize(utf8JsonWriter, this, SampleJsonSerializerContext.Default.SampleJson);
    }
}