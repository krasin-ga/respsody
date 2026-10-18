using Respsody.Client;
using Respsody.Resp;
using StackExchange.Redis;

namespace Respsody.Benchmarks.Library;

internal static class BenchmarkValidation
{
    public static void Validate(RespClient client, IDatabase database, Key key, RedisKey redisKey, byte[] expected)
    {
        using var value = client.Get(key).AsTask().GetAwaiter().GetResult();
        if (!value.GetSpan().SequenceEqual(expected))
            throw new InvalidOperationException("Respsody returned an unexpected GET value.");
        var redisValue = database.StringGet(redisKey);
        if (!((ReadOnlyMemory<byte>)redisValue).Span.SequenceEqual(expected))
            throw new InvalidOperationException("StackExchange.Redis returned an unexpected GET value.");

        using var values = client.Mget(Enumerable.Repeat(key, 10).ToArray()).AsTask().GetAwaiter().GetResult();
        if (values.Length != 10)
            throw new InvalidOperationException("Respsody returned an unexpected MGET count.");
        for (var index = 0; index < values.Length; index++)
            if (!values[index].ToRespString().GetSpan().SequenceEqual(expected))
                throw new InvalidOperationException("Respsody returned an unexpected MGET value.");

        var redisValues = database.StringGet(Enumerable.Repeat(redisKey, 10).ToArray());
        if (redisValues.Length != 10)
            throw new InvalidOperationException("StackExchange.Redis returned an unexpected MGET count.");
        foreach (var item in redisValues)
            if (!((ReadOnlyMemory<byte>)item).Span.SequenceEqual(expected))
                throw new InvalidOperationException("StackExchange.Redis returned an unexpected MGET value.");
    }
}
