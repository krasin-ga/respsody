using System.Diagnostics;
using System.Text;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Resp;
using StackExchange.Redis;

namespace Respsody.Benchmarks.NonScientific.Comparison;

internal sealed class ComparisonWorkload : IDisposable
{
    private readonly WorkerRequest _request;
    private readonly byte[] _payload;
    private readonly Key[] _keys;
    private readonly RedisKey[] _redisKeys;
    private RespClient? _client;
    private ConnectionMultiplexer? _multiplexer;
    private IDatabase? _database;

    public ComparisonWorkload(WorkerRequest request)
    {
        _request = request;
        _payload = Enumerable.Range(0, request.PayloadBytes).Select(index => (byte)(index * 31 + 127)).ToArray();
        var keys = Enumerable.Range(0, request.Concurrency)
            .Select(index => Encoding.UTF8.GetBytes($"{request.KeyPrefix}:{index:D6}")).ToArray();
        _keys = keys.Select(key => new Key(key)).ToArray();
        _redisKeys = keys.Select(key => (RedisKey)key).ToArray();
    }

    public async Task<string> Connect()
    {
        if (_request.Client == "Respsody")
        {
            _client = await new RespClientFactory().Create(new RespClientOptions(),
                new DefaultConnectionProcedure(new ConnectionOptions { Endpoint = _request.Endpoint }));
            using var command = _client.CreateCommand<RespString>("INFO").Token("server");
            using var response = await _client.ExecuteCommand(command);
            return Encoding.UTF8.GetString(response.GetSpan());
        }

        var options = new ConfigurationOptions
        {
            Protocol = RedisProtocol.Resp3,
            DefaultDatabase = 0,
            AsyncTimeout = int.MaxValue,
            SyncTimeout = int.MaxValue,
            ConnectTimeout = 10_000,
            AbortOnConnectFail = true
        };
        options.EndPoints.Add(_request.Endpoint);
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        _database = _multiplexer.GetDatabase();
        if (_multiplexer.GetCounters().Interactive.SocketCount != 1)
            throw new InvalidOperationException("The comparison requires one command connection.");
        return (string)(await _database.ExecuteAsync("INFO", "server"))!;
    }

    public async Task<long> Run(int pairsPerLane, bool validate, long[][]? latencies = null)
    {
        var tasks = new Task<long>[_request.Concurrency];
        for (var lane = 0; lane < tasks.Length; lane++)
            tasks[lane] = _request.Client == "Respsody"
                ? RunRespsody(lane, pairsPerLane, validate, latencies?[lane])
                : RunStackExchange(lane, pairsPerLane, validate, latencies?[lane]);
        var totals = await Task.WhenAll(tasks);
        var total = totals.Sum();
        var expected = checked((long)pairsPerLane * _request.Concurrency * Consume(_payload, false));
        if (total != expected)
            throw new InvalidOperationException($"Unexpected response checksum: {total}; expected {expected}.");
        return total;
    }

    private async Task<long> RunRespsody(int lane, int count, bool validate, long[]? latencies)
    {
        var client = _client!;
        var key = _keys[lane];
        var value = _payload.Length == 0 ? Value.Utf8(string.Empty) : Value.ByteArray(_payload);
        var owned = _request.Response == "owned";
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            var start = latencies is null ? 0 : Stopwatch.GetTimestamp();
            await client.Set(key, value);
            using (var response = await client.Get(key))
            {
                if (response.IsNull())
                    throw new InvalidOperationException("GET returned null.");
                if (owned)
                {
                    var bytes = response.GetSpan().ToArray();
                    total += Consume(bytes, validate);
                }
                else
                    total += Consume(response.GetSpan(), validate);
            }
            if (latencies is not null)
                latencies[i] = Stopwatch.GetTimestamp() - start;
        }
        return total;
    }

    private async Task<long> RunStackExchange(int lane, int count, bool validate, long[]? latencies)
    {
        var database = _database!;
        var key = _redisKeys[lane];
        var value = (RedisValue)_payload;
        var owned = _request.Response == "owned";
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            var start = latencies is null ? 0 : Stopwatch.GetTimestamp();
            if (!await database.StringSetAsync(key, value))
                throw new InvalidOperationException("SET did not succeed.");
            if (owned)
            {
                var bytes = (byte[]?)await database.StringGetAsync(key)
                    ?? throw new InvalidOperationException("GET returned null.");
                total += Consume(bytes, validate);
            }
            else
            {
                using var response = await database.StringGetLeaseAsync(key)
                    ?? throw new InvalidOperationException("GET returned null.");
                total += Consume(response.Span, validate);
            }
            if (latencies is not null)
                latencies[i] = Stopwatch.GetTimestamp() - start;
        }
        return total;
    }

    private long Consume(ReadOnlySpan<byte> bytes, bool validate)
    {
        if (bytes.Length != _payload.Length || (validate && !bytes.SequenceEqual(_payload)))
            throw new InvalidOperationException("GET returned unexpected bytes.");
        return bytes.Length + (bytes.IsEmpty ? 0L : bytes[0] + bytes[^1]);
    }

    public async Task Cleanup()
    {
        if (_client is not null)
        {
            using var command = _client.CreateCommand<RespNumber>("DEL");
            foreach (var key in _keys)
                command.Key(key);
            using var response = await _client.ExecuteCommand(command);
        }
        else if (_database is not null)
            await _database.KeyDeleteAsync(_redisKeys);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _multiplexer?.Dispose();
    }
}
