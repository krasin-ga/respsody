using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;
using Respsody.Benchmarks.Library;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using StackExchange.Redis;

namespace Respsody.Benchmarks;

[MemoryDiagnoser, ThreadingDiagnoser, OperationsPerSecond]
public class SequentialBenchmark
{
    private static FrozenDictionary<int, (Key Key, RedisKey RKey, byte[] Value)> _payloads = null!;
    private RespClient _respsody = null!;
    private IDatabase _stackExchangeDb = null!;

    [Params(256, 32786)]
    public int MessageSize = 0;

    [GlobalSetup]
    public void Setup()
    {
        var endPoint = BenchmarkEnvironment.RespServerEndpoint;

        _respsody = new RespClientFactory().Create(
            new RespClientOptions(),
            new DefaultConnectionProcedure(new ConnectionOptions { Endpoint = endPoint.ToString() })
        ).Result;

        _stackExchangeDb = ConnectionMultiplexer.Connect(new ConfigurationOptions { EndPoints = [endPoint] }).GetDatabase();

        _payloads = GetType().GetField(nameof(MessageSize))?.GetCustomAttribute<ParamsAttribute>()!
            .Values.Select(v => (int)v!)
            .ToFrozenDictionary(
                k => k,
                static v => (
                    new Key($"seq_bench_{v}", Encoding.UTF8),
                    new RedisKey($"seq_bench_{v}"),
                    Enumerable.Range(0, v).Select(i => (byte)i).ToArray()
                ))!;
    }

    [Benchmark]
    public async Task Respsody_Set_Get()
    {
        var payload = GetPayload(MessageSize);

        await _respsody.Set(payload.Key, new Value(payload.Value));
        using var respString = await _respsody.Get(payload.Key);
    }

    [Benchmark]
    public async Task SE_Set_Get()
    {
        var payload = GetPayload(MessageSize);

        await _stackExchangeDb.StringSetAsync(payload.RKey, payload.Value);
        await _stackExchangeDb.StringGetAsync(payload.RKey);
    }

    [Benchmark]
    public async Task Respsody_MSet_MGet_10()
    {
        var payload = GetPayload(MessageSize);
        await _respsody.Mset(
            [
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
                (payload.Key, new Value(payload.Value)),
            ]
        );

        using var res = await _respsody.Mget(
            [
                payload.Key,
                payload.Key,
                payload.Key,
                payload.Key,
                payload.Key,
                payload.Key,
                payload.Key,
                payload.Key,
                payload.Key,
                payload.Key
            ]
        );
    }

    [Benchmark]
    public async Task SE_MSet_MGet_10()
    {
        var payload = GetPayload(MessageSize);
        await _stackExchangeDb.StringSetAsync(
            [
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
                new KeyValuePair<RedisKey, RedisValue>(payload.RKey, payload.Value),
            ]
        );

        await _stackExchangeDb.StringGetAsync(
            [
                payload.RKey,
                payload.RKey,
                payload.RKey,
                payload.RKey,
                payload.RKey,
                payload.RKey,
                payload.RKey,
                payload.RKey,
                payload.RKey,
                payload.RKey
            ]
        );
    }

    private static (Key Key, RedisKey RKey, byte[] Value) GetPayload(int size)
    {
        return _payloads[size];
    }
}