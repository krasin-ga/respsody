namespace Respsody.Benchmarks.NonScientific.Library;

internal delegate Task<int> TypedTest<in T>(Target target, T[] values);