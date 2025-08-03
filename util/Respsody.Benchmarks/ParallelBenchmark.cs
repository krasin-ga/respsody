using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Respsody.Benchmarks.Library;
using Respsody.Client;
using StackExchange.Redis;

namespace Respsody.Benchmarks;

[MemoryDiagnoser, ThreadingDiagnoser, OperationsPerSecond, GcServer]
public class ParallelBenchmark
{
    private const int OperationsPerInvoke = 12_500;
    private readonly List<Task> _tasks = new(OperationsPerInvoke);
    private RespClient _client = null!;
    private IDatabase _stackExchangeDb = null!;

    [GlobalSetup]
    public void Setup()
    {
        ThreadPool.SetMaxThreads(Environment.ProcessorCount, Environment.ProcessorCount);

        _client = new RespClientFactory().Create(BenchmarkEnvironment.RespServerEndpoint).Result;
        _stackExchangeDb = ConnectionMultiplexer.Connect(new ConfigurationOptions { EndPoints = [BenchmarkEnvironment.RespServerEndpoint] }).GetDatabase();

        _client.Set(Key.Utf8("string"), Value.Utf8("string")).AsTask().Wait();
        _client.Set(Key.Unicode("string"), Value.Unicode("string")).AsTask().Wait();
        _client.Set(Key.Utf8("json"), Value.Utf8(JsonSerializer.Serialize(new TestJsonObj()))).AsTask().Wait();
    }

    [Benchmark(Description = "[when_all 12_500 tasks] [Respsody] Get (noop) commands/s", OperationsPerInvoke = OperationsPerInvoke)]
    public async ValueTask Respsody_StringGetGeneratedCommand()
    {
        _tasks.Clear();

        for (var i = 0; i < OperationsPerInvoke; i++)
            _tasks.Add(Exec());

        await Task.WhenAll(_tasks);

        return;

        async Task Exec()
        {
            using var _ = await _client.Get(Key.Utf8("string"));
        }
    }

    [Benchmark(Description = "[when_all 12_500 tasks] [StackExchange.Redis] Get (noop) commands/s", OperationsPerInvoke = OperationsPerInvoke)]
    public async ValueTask StackExchange_StringGetGeneratedCommand()
    {
        _tasks.Clear();

        for (var i = 0; i < OperationsPerInvoke; i++)
            _tasks.Add(Exec());

        await Task.WhenAll(_tasks);

        return;

        async Task Exec()
        {
            await _stackExchangeDb.StringGetAsync("string");
        }
    }

    [Benchmark(Description = "[when_all 12_500 tasks] [Respsody] Get (json) commands/s", OperationsPerInvoke = OperationsPerInvoke)]
    public async ValueTask Respsody_StringGetJsonGeneratedCommand()
    {
        _tasks.Clear();

        for (var i = 0; i < OperationsPerInvoke; i++)
            _tasks.Add(Exec());

        await Task.WhenAll(_tasks);

        return;

        async Task Exec()
        {
            using var json = await _client.Get(Key.Utf8("json"));
            JsonSerializer.Deserialize(json.GetSpan(), SourceGenerationContext.Default.TestJsonObj);
        }
    }

    [Benchmark(Description = "[when_all 12_500 tasks] [StackExchange.Redis] Get (json) commands/s", OperationsPerInvoke = OperationsPerInvoke)]
    public async ValueTask StackExchange_StringGetJsonGeneratedCommand()
    {
        _tasks.Clear();

        for (var i = 0; i < OperationsPerInvoke; i++)
            _tasks.Add(Exec());

        await Task.WhenAll(_tasks);

        return;

        async Task Exec()
        {
            var json = await _stackExchangeDb.StringGetAsync("json");
            JsonSerializer.Deserialize(json!, SourceGenerationContext.Default.TestJsonObj);
        }
    }
}