using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Respsody.Benchmarks;

try
{
    BenchmarkEnvironment.Setup();

    BenchmarkRunner.Run<ParallelBenchmark>();
}
finally
{
    BenchmarkEnvironment.Cleanup();
}