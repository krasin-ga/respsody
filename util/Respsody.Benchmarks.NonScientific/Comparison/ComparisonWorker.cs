using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Respsody.Benchmarks.NonScientific.Library;
using StackExchange.Redis;

namespace Respsody.Benchmarks.NonScientific.Comparison;

internal sealed record WorkerRequest(string Endpoint, string Client, string Response, int PayloadBytes,
    int Concurrency, int PairsPerLane, double WarmupSeconds, string Phase, int Round, string KeyPrefix);

internal sealed record WorkerResult(WorkerRequest Request, DateTimeOffset StartedUtc, int ProcessId,
    string Runtime, string OS, string Architecture, int Processors, bool ServerGC, string GCLatencyMode,
    int MinWorkerThreads, int MaxWorkerThreads, string RespsodyVersion, string StackExchangeVersion,
    string ServerInfo, long CompletedCommands, long Checksum, double ElapsedSeconds, double? CpuSeconds,
    long? AllocatedBytes, int[] Collections, long LatencySamples, double? PairP50Microseconds,
    double? PairP95Microseconds, double? PairP99Microseconds);

internal static class ComparisonWorker
{
    public static async Task Run(string requestPath)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(await File.ReadAllTextAsync(requestPath))!;
        if (request.Client is not ("Respsody" or "StackExchange.Redis") ||
            request.Response is not ("borrowed" or "owned") ||
            request.Phase is not ("calibration" or "cpu" or "allocation" or "latency") ||
            request.PairsPerLane < 1 || request.Concurrency < 1 ||
            (long)request.PairsPerLane * request.Concurrency > 10_000_000)
            throw new ArgumentException("Invalid worker request.");

        using var workload = new ComparisonWorkload(request);
        var serverInfo = await workload.Connect();
        var failed = false;
        try
        {
            await workload.Run(4, validate: true);
            var warmup = Stopwatch.StartNew();
            do
            {
                await workload.Run(32, validate: false);
            } while (warmup.Elapsed.TotalSeconds < request.WarmupSeconds);

            long[][]? latencies = null;
            if (request.Phase == "latency")
            {
                var warmupLatencies = Enumerable.Range(0, request.Concurrency).Select(_ => new long[32]).ToArray();
                await workload.Run(32, validate: false, warmupLatencies);
                latencies = Enumerable.Range(0, request.Concurrency).Select(_ => new long[request.PairsPerLane]).ToArray();
            }

            _ = Environment.CpuUsage.TotalTime;
            _ = GC.GetTotalAllocatedBytes(precise: true);
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var started = DateTimeOffset.UtcNow;
            var allocationRun = request.Phase == "allocation";
            var cpuRun = request.Phase is "cpu" or "calibration";
            var beforeAllocations = allocationRun ? GC.GetTotalAllocatedBytes(precise: true) : 0;
            var beforeCpu = cpuRun ? Environment.CpuUsage.TotalTime : TimeSpan.Zero;
            var start = Stopwatch.GetTimestamp();
            var checksum = await workload.Run(request.PairsPerLane, validate: false, latencies);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            var afterCpu = cpuRun ? Environment.CpuUsage.TotalTime : TimeSpan.Zero;
            var afterAllocations = allocationRun ? GC.GetTotalAllocatedBytes(precise: true) : 0;
            for (var generation = 0; generation < collections.Length; generation++)
                collections[generation] = GC.CollectionCount(generation) - collections[generation];

            var cpu = new Delta<TimeSpan>(beforeCpu, afterCpu).EnsureIncreasingOrEq();
            var allocations = new Delta<long>(beforeAllocations, afterAllocations).EnsureIncreasingOrEq();
            await workload.Run(4, validate: true);
            ThreadPool.GetMinThreads(out var minimumThreads, out _);
            ThreadPool.GetMaxThreads(out var maximumThreads, out _);
            var samples = latencies?.SelectMany(lane => lane).Order().ToArray();
            var result = new WorkerResult(request, started, Environment.ProcessId,
                RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
                GCSettings.IsServerGC, GCSettings.LatencyMode.ToString(), minimumThreads, maximumThreads,
                VersionOf(typeof(RespClientFactory)), VersionOf(typeof(ConnectionMultiplexer)), serverInfo,
                checked(2L * request.PairsPerLane * request.Concurrency), checksum, elapsed,
                cpuRun ? (cpu.After - cpu.Before).TotalSeconds : null,
                allocationRun ? allocations.After - allocations.Before : null, collections,
                samples?.LongLength ?? 0, Percentile(samples, 0.50), Percentile(samples, 0.95), Percentile(samples, 0.99));
            await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(result, ComparisonRunner.Json));
        }
        catch (Exception error)
        {
            failed = true;
            Console.Error.WriteLine(error);
            throw;
        }
        finally
        {
            try
            {
                await workload.Cleanup();
            }
            catch (Exception error) when (failed)
            {
                Console.Error.WriteLine($"Cleanup also failed: {error}");
            }
        }
    }

    private static string VersionOf(Type type) =>
        type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? type.Assembly.GetName().Version?.ToString() ?? "unknown";

    private static double? Percentile(long[]? sorted, double percentile) =>
        sorted is null || (percentile >= 0.95 && sorted.Length * (1 - percentile) < 100) ? null
        : sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1] * 1_000_000.0 / Stopwatch.Frequency;
}
