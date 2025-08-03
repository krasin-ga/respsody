using System.Diagnostics;
using System.Net;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Garnet;
using Garnet.server;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Respsody;
using Respsody.Benchmarks.NonScientific;
using Respsody.Benchmarks.NonScientific.Library;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using StackExchange.Redis;

// ReSharper disable All

ThreadPool.SetMaxThreads(Environment.ProcessorCount, Environment.ProcessorCount);

var ipEndPoint = new IPEndPoint(IPAddress.Loopback, 6379);

//using var garnet = new GarnetServer(
//    new GarnetServerOptions
//    {
//        EndPoints = [ipEndPoint],
//        logger = new ConsoleLoggerProvider(
//                new OptionsMonitor<ConsoleLoggerOptions>(
//                    new OptionsFactory<ConsoleLoggerOptions>([], []),
//                    [],
//                    new OptionsCache<ConsoleLoggerOptions>()))
//            .CreateLogger("garnet")
//    });

//garnet.Start();

var rnd = new Random(42);
var large_300 = Enumerable.Range(0, 1024 * 1024).OrderBy(s => rnd.Next()).Take(300)
    .Select(CreatePair)
    .ToArray();

var small_20K = Enumerable.Range(0, 20000).Select(i => CreatePair(i % 256 + 1)).ToArray();

var jsons_10K = Enumerable.Range(0, 10000).Select(i => new SampleJson()
{
    StringValue = $"json_{i}",
    ScalarValue = i * i,
    VectorValue = [..Enumerable.Range(0, 16).Select(v => rnd.NextSingle())]
}).Select(
    j => new KeyValuePair<string, byte[]>(
        j.StringValue,
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(j))
    )
).ToArray();

var clientFactory = new RespClientFactory();
var client = await clientFactory.Create(
    new RespClientOptions()
    {
        Handler = new RespClientHandler()
    },
    new DefaultConnectionProcedure(new ConnectionOptions() { Endpoint = ipEndPoint.ToString() })
);

var db = (await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions()
{
    AsyncTimeout = int.MaxValue,
    SyncTimeout = int.MaxValue,
    EndPoints = [ipEndPoint]
})).GetDatabase();

var tests = new Tests(db, client);

var iters = 50;
BenchmarkResult[] results =
[
    await RunTest(tests.SetGet_Sequential, Target.Respsody, small_20K, iterations: iters),
    await RunTest(tests.SetGet_Sequential, Target.StackOverflowRedis, small_20K, iterations: iters),

    await RunTest(tests.SetGet_Sequential, Target.Respsody, large_300, iterations: iters),
    await RunTest(tests.SetGet_Sequential, Target.StackOverflowRedis, large_300, iterations: iters),

    await RunTest(tests.SetGet_Sequential_WithJsonDeserialize, Target.Respsody, jsons_10K, iterations: iters),
    await RunTest(tests.SetGet_Sequential_WithJsonDeserialize, Target.StackOverflowRedis, jsons_10K, iterations: iters),

    await RunTest(tests.SetGet_WhenEach, Target.Respsody, small_20K, iterations: iters),
    await RunTest(tests.SetGet_WhenEach, Target.StackOverflowRedis, small_20K, iterations: iters),

    await RunTest(tests.OneSetTwoGets_WhenEach, Target.Respsody, small_20K, iterations: iters),
    await RunTest(tests.OneSetTwoGets_WhenEach, Target.StackOverflowRedis, small_20K, iterations: iters),

    await RunTest(tests.SetGet_WhenEach_WithJsonDeserialize, Target.Respsody, jsons_10K, iterations: iters),
    await RunTest(tests.SetGet_WhenEach_WithJsonDeserialize, Target.StackOverflowRedis, jsons_10K, iterations: iters),

    await RunTest(tests.OneSetTwoGets_WhenEach, Target.Respsody, large_300, iterations: iters),
    await RunTest(tests.OneSetTwoGets_WhenEach, Target.StackOverflowRedis, large_300, iterations: iters),

    await RunTest(tests.MSetGet, Target.Respsody, small_20K, iterations: iters),
    await RunTest(tests.MSetGet, Target.StackOverflowRedis, small_20K, iterations: iters),

    await RunTest(tests.MSetGet, Target.Respsody, large_300, iterations: iters),
    await RunTest(tests.MSetGet, Target.StackOverflowRedis, large_300, iterations: iters),
];
Console.WriteLine(BenchmarkResult.WriteTable(results));

return;

static KeyValuePair<string, byte[]> CreatePair(int size) =>
    new(size + "__k", Enumerable.Range(0, size).Select(b => (byte)b).ToArray());

static async Task<BenchmarkResult> RunTest(
    Test test,
    Target target,
    KeyValuePair<string, byte[]>[] values,
    int iterations = 16,
    [CallerArgumentExpression(nameof(test))]
    string? exp = null,
    [CallerArgumentExpression(nameof(values))]
    string? valuesExp = null)
{
    Console.WriteLine($"Starting test {exp} / {target}");
    Console.WriteLine($"Dataset: {valuesExp}");

    const int warmUpIterations = 2;
    Console.WriteLine("Warming up...");

    for (var i = 0; i < warmUpIterations; i++)
        await test(target, values);

    await Task.Delay(TimeSpan.FromMilliseconds(100));

    Console.WriteLine("Running...");

    var measurements = new List<IterationMeasurements>();

    GC.Collect();
    GC.WaitForPendingFinalizers();

    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        var beforeAllocs = GC.GetTotalAllocatedBytes(precise: true);
        var beforeCpuTime = CpuTime.GetActualValue();

        sw.Restart();
        await test(target, values);
        sw.Stop();

        var afterCpuTime = CpuTime.GetActualValue();
        var afterAllocs = GC.GetTotalAllocatedBytes(precise: true);

        var allocationDelta = new Delta<long>(beforeAllocs, afterAllocs);
        if (!allocationDelta.IsIncreasingOrEq)
        {
            Console.WriteLine("Skipping iteration due to GC.GetTotalAllocatedBytes() decrease");
            i--;
            continue;
        }

        measurements.Add(
            new IterationMeasurements(
                sw.Elapsed,
                new Delta<TimeSpan>(beforeCpuTime, afterCpuTime).EnsureIncreasingOrEq(),
                allocationDelta.EnsureIncreasingOrEq()
            ));
    }

    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    GC.Collect();

    return new BenchmarkResult(
        Scenario: exp!.Split(".").Last(),
        Target: target.ToString(),
        Dataset: new Dataset(valuesExp!, values.Length, new SizeInBytes(values.Sum(v => v.Value.Length))),
        Iterations: iterations,
        TotalElapsed: measurements.Aggregate(TimeSpan.Zero, (a, b) => a + b.Elapsed),
        BestRun: measurements.Select(r => r.Elapsed).Min(),
        CpuTime: measurements.Aggregate(TimeSpan.Zero, (acc, m) => acc + (m.Cpu.After - m.Cpu.Before)),
        MinWastedCpuTime: measurements.Select(m => m.Cpu.After - m.Cpu.Before).Where(t => t > TimeSpan.Zero).Min(),
        TotalAllocated: new SizeInBytes(measurements.Aggregate(0L, (acc, m) => acc + (m.Allocations.After - m.Allocations.Before))),
        TotalMemory: new SizeInBytes(GC.GetTotalMemory(forceFullCollection: false))
    );
}