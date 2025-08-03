using System.Text.Json.Serialization;

namespace Respsody.Benchmarks.Library;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(TestJsonObj))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}