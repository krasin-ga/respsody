using System.Text.Json.Serialization;

namespace Respsody.Benchmarks.NonScientific.Library;

[JsonSerializable(typeof(SampleJson))]
public partial class SampleJsonSerializerContext : JsonSerializerContext;