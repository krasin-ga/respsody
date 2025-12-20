using BenchmarkDotNet.Running;
using Respsody.Benchmarks;

try
{
    BenchmarkEnvironment.Setup();

    BenchmarkRunner.Run<SequentialBenchmark>();
}
finally
{
    BenchmarkEnvironment.Cleanup();
}