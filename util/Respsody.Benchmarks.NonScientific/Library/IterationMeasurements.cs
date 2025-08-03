namespace Respsody.Benchmarks.NonScientific.Library;

public readonly record struct IterationMeasurements(TimeSpan Elapsed, Delta<TimeSpan> Cpu, Delta<long> Allocations);