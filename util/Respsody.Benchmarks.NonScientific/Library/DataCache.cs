using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Respsody.Benchmarks.NonScientific.Library;

using RespsodyTuples = ConcurrentDictionary<object, ((Key Key, Value Value)[] Pairs, Key[] Keys)>;
using StackExchangeTuples = ConcurrentDictionary<object, (KeyValuePair<RedisKey, RedisValue>[] Pairs, RedisKey[] Keys)>;

public static class DataCache
{
    private static readonly RespsodyTuples CacheForRespsody = [];
    private static readonly StackExchangeTuples CacheForSe = [];

    public static ((Key Key, Value Value)[] Pairs, Key[] Keys) AsRespsody(
        KeyValuePair<string, byte[]>[] pairs)
    {
        return CacheForRespsody.GetOrAdd(
            pairs,
            static (_, arg) =>
            {
                var respsodyValues = ToRespsodyValues(arg);
                return (respsodyValues, respsodyValues.Select(v => v.Key).ToArray());
            },
            pairs);
    }

    public static (KeyValuePair<RedisKey, RedisValue>[] Pairs, RedisKey[] Keys) AsSe(
        KeyValuePair<string, byte[]>[] pairs)
    {
        return CacheForSe.GetOrAdd(
            pairs,
            static (_, arg) =>
            {
                var values = ToStackExchangeValues(arg);
                return (values, values.Select(v => v.Key).ToArray());
            },
            pairs);
    }

    private static KeyValuePair<RedisKey, RedisValue> ToStackExchangePair(KeyValuePair<string, byte[]> kvp) =>
        new(new RedisKey(kvp.Key), kvp.Value);

    private static (Key Key, Value Value) ToRespsodyPair(KeyValuePair<string, byte[]> kvp) =>
        new(Key.Utf8(kvp.Key), Value.ByteArray(kvp.Value));

    private static KeyValuePair<RedisKey, RedisValue>[] ToStackExchangeValues(IEnumerable<KeyValuePair<string, byte[]>> kvps) =>
        [.. kvps.Select(ToStackExchangePair)];

    private static (Key Key, Value Value)[] ToRespsodyValues(IEnumerable<KeyValuePair<string, byte[]>> kvps) =>
        [.. kvps.Select(ToRespsodyPair)];
}