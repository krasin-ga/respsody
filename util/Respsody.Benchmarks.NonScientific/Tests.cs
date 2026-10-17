using System.Text.Json;
using Respsody.Benchmarks.NonScientific.Library;
using Respsody.Client;
using Respsody.Resp;
using StackExchange.Redis;

namespace Respsody.Benchmarks.NonScientific;

public class Tests(IDatabaseAsync db, IRespClient client)
{
    public async Task<int> MSetGet(Target target, KeyValuePair<string, byte[]>[] values)
    {
        if (target is Target.Respsody)
        {
            var (rspValues, rspKeys) = DataCache.AsRespsody(values);

            await client.Mset(rspValues);
            using var results = await client.Mget(rspKeys);

            return results.Length;
        }

        var (seValues, seKeys) = DataCache.AsSe(values);

        await db.StringSetAsync(seValues);
        var resultsSe = await db.StringGetAsync(seKeys);
        return resultsSe.Length;
    }

    public async Task<int> SetGet_Sequential(Target target, KeyValuePair<string, byte[]>[] values)
    {
        if (target is Target.Respsody)
        {
            var i = 0L;
            var (rspValues, _) = DataCache.AsRespsody(values);

            foreach (var (key, value) in rspValues)
            {
                await client.Set(key, value);
                using var val = await client.Get(key);
                i += val.GetSpan().Length;
            }

            return (int)i;
        }

        var j = 0L;
        var (seValues, _) = DataCache.AsSe(values);
        foreach (var (key, value) in seValues)
        {
            await db.StringSetAsync(key, value);
            var res = await db.StringGetAsync(key);
            j += res.Length();
        }

        return (int)j;
    }

    public async Task<int> SetGet_Sequential_WithJsonDeserialize(Target target, KeyValuePair<string, byte[]>[] values)
    {
        if (target is Target.Respsody)
        {
            var i = 0L;
            var (rspValues, _) = DataCache.AsRespsody(values);

            foreach (var (key, value) in rspValues)
            {
                await client.Set(key, value);
                using var val = await client.Get(key);
                i += JsonSerializer.Deserialize(val.GetSpan(), SampleJsonSerializerContext.Default.SampleJson)!.VectorValue.Length;
            }
            return (int)i;
        }

        var j = 0L;
        var (seValues, _) = DataCache.AsSe(values);
        foreach (var (key, value) in seValues)
        {
            await db.StringSetAsync(key, value);
            var res = (ReadOnlyMemory<byte>)await db.StringGetAsync(key);
            j += JsonSerializer.Deserialize(res.Span, SampleJsonSerializerContext.Default.SampleJson)!.VectorValue.Length;
        }

        return (int)j;
    }

    public async Task<int> OneSetTwoGets_WhenEach(Target target, KeyValuePair<string, byte[]>[] values)
    {
        if (target is Target.Respsody)
        {
            var i = 0;
            var (rspValues, _) = DataCache.AsRespsody(values);

            await foreach (var res in Task.WhenEach(
                               rspValues.SelectTaskRun(
                                   async v =>
                                   {
                                       await client.Set(v.Key, v.Value);

                                       using var val = await client.Get(v.Key);
                                       using var val2 = await client.Get(v.Key);
                                   })))
            {
                await res;
                i++;
            }

            return i;
        }

        var j = 0;
        var (seValues, _) = DataCache.AsSe(values);

        await foreach (var res in Task.WhenEach(
                           seValues.SelectTaskRun(
                               async v =>
                               {
                                   await db.StringSetAsync(v.Key, v.Value);

                                   await db.StringGetAsync(v.Key);
                                   await db.StringGetAsync(v.Key);
                               })))
        {
            await res;
            j++;
        }

        return j;
    }

    public async Task<int> SetGet_WhenEach_WithJsonDeserialize(Target target, KeyValuePair<string, byte[]>[] values)
    {
        if (target is Target.Respsody)
        {
            var i = 0;
            var i2 = 0;
            var (rspValues, _) = DataCache.AsRespsody(values);

            await foreach (var res in Task.WhenEach(
                               rspValues.SelectTaskRun(
                                   async v =>
                                   {
                                       await client.Set(v.Key, v.Value);
                                       using var val = await client.Get(v.Key);

                                       return JsonSerializer.Deserialize(val.GetSpan(), SampleJsonSerializerContext.Default.SampleJson)!.VectorValue.Length;
                                   })))
            {
                i2 += await res;
                i++;
            }

            return i + i2;
        }

        var j = 0;
        var j2 = 0;
        var (seValues, _) = DataCache.AsSe(values);

        await foreach (var res in Task.WhenEach(
                           seValues.SelectTaskRun(
                               async v =>
                               {
                                   await db.StringSetAsync(v.Key, v.Value);
                                   var stringGetAsync = (ReadOnlyMemory<byte>) await db.StringGetAsync(v.Key);
                                   return JsonSerializer.Deserialize(stringGetAsync.Span, SampleJsonSerializerContext.Default.SampleJson)!.VectorValue.Length;
                               })))
        {
            j2 += await res;
            j++;
        }

        return j + j2;
    }
    
    public async Task<int> SetGet_WhenEach_WithJsonSerializeDeserialize(Target target, SampleJson[] values)
    {
        if (target is Target.Respsody)
        {
            var i = 0;
            var i2 = 0;

            await foreach (var res in Task.WhenEach(
                               values.SelectTaskRun(
                                   async v =>
                                   {
                                       var key = Key.Utf8(v.StringValue);

                                       await client.Set(key, new Value(JsonSerializer.SerializeToUtf8Bytes(v, SampleJsonSerializerContext.Default.SampleJson)));
                                       using var val = await client.Get(key);

                                       var deserialized = JsonSerializer.Deserialize(val.GetSpan(), SampleJsonSerializerContext.Default.SampleJson)!;
                                       if (deserialized.StringValue != v.StringValue)
                                           throw new InvalidOperationException();
                                       return deserialized.VectorValue.Length;
                                   })))
            {
                i2 += await res;
                i++;
            }

            return i + i2;
        }

        var j = 0;
        var j2 = 0;

        await foreach (var res in Task.WhenEach(
                           values.SelectTaskRun(
                               async v =>
                               {
                                   await db.StringSetAsync(v.StringValue, JsonSerializer.SerializeToUtf8Bytes(v, SampleJsonSerializerContext.Default.SampleJson));
                                   var stringGetAsync = (ReadOnlyMemory<byte>) await db.StringGetAsync(v.StringValue);
                                   var deserialized = JsonSerializer.Deserialize(stringGetAsync.Span, SampleJsonSerializerContext.Default.SampleJson)!;

                                   if (deserialized.StringValue != v.StringValue)
                                       throw new InvalidOperationException();
                                   return deserialized.VectorValue.Length;
                               })))
        {
            j2 += await res;
            j++;
        }

        return j + j2;
    }

    public async Task<int> SetGet_WhenEach(Target target, KeyValuePair<string, byte[]>[] values)
    {
        if (target is Target.Respsody)
        {
            var i = 0;
            var (rspValues, _) = DataCache.AsRespsody(values);

            await foreach (var res in Task.WhenEach(
                               rspValues.SelectTaskRun(
                                   async v =>
                                   {
                                       await client.Set(v.Key, v.Value);
                                       using var val = await client.Get(v.Key);
                                   })))
            {
                await res;
                i++;
            }

            return i;
        }

        var j = 0;
        var (seValues, _) = DataCache.AsSe(values);

        await foreach (var res in Task.WhenEach(
                           seValues.SelectTaskRun(
                               async v =>
                               {
                                   await db.StringSetAsync(v.Key, v.Value);
                                   await db.StringGetAsync(v.Key);
                               })))
        {
            await res;
            j++;
        }

        return j;
    }

    public async Task<int> SetGet_WhenEach_Adapted(Target target, KeyValuePair<string, byte[]>[] values)
    {
        if (target is Target.Respsody)
        {
            var i = 0;
            var l = 0;
            var (seValuesForRsp, _) = DataCache.AsSe(values);

            await foreach (var res in Task.WhenEach(
                               seValuesForRsp.SelectTaskRun(
                                   async v =>
                                   {
                                       var key = new Key(v.Key!);

                                       await client.Set(key, new Value(v.Value!));
                                       using var val = await client.Get(key);
                                       RedisValue seValue = val.GetSpan().ToArray();
                                       return (int)seValue.Length();
                                   })))
            {
                l += await res;
                i++;
            }

            return i+l;
        }

        var j = 0;
        var (seValues, _) = DataCache.AsSe(values);

        await foreach (var res in Task.WhenEach(
                           seValues.SelectTaskRun(
                               async v =>
                               {
                                   await db.StringSetAsync(v.Key, v.Value);
                                   var result = await db.StringGetAsync(v.Key);
                                   return (int)result.Length();
                               })))
        {
            j += 1 + await res;
        }

        return j;
    }

    public static int ExpectedResult<T>(string scenario, T[] values)
    {
        return scenario switch
        {
            nameof(MSetGet) or nameof(SetGet_WhenEach) or nameof(OneSetTwoGets_WhenEach) => values.Length,
            nameof(SetGet_Sequential) => values.Cast<KeyValuePair<string, byte[]>>().Sum(v => v.Value.Length),
            nameof(SetGet_WhenEach_Adapted) => values.Length + values.Cast<KeyValuePair<string, byte[]>>().Sum(v => v.Value.Length),
            nameof(SetGet_Sequential_WithJsonDeserialize) => JsonVectorLength(values),
            nameof(SetGet_WhenEach_WithJsonDeserialize) => values.Length + JsonVectorLength(values),
            nameof(SetGet_WhenEach_WithJsonSerializeDeserialize) => values.Length + values.Cast<SampleJson>().Sum(v => v.VectorValue.Length),
            _ => throw new ArgumentException($"No expected result is defined for {scenario}.", nameof(scenario))
        };

        static int JsonVectorLength(T[] source) => source.Cast<KeyValuePair<string, byte[]>>()
            .Sum(v => JsonSerializer.Deserialize(v.Value, SampleJsonSerializerContext.Default.SampleJson)!.VectorValue.Length);
    }

    public async Task ValidateStoredValues<T>(Target target, T[] values)
    {
        var expected = values.Select(value => value switch
        {
            KeyValuePair<string, byte[]> pair => pair,
            SampleJson json => new KeyValuePair<string, byte[]>(json.StringValue,
                JsonSerializer.SerializeToUtf8Bytes(json, SampleJsonSerializerContext.Default.SampleJson)),
            _ => throw new ArgumentException("Unsupported benchmark dataset.", nameof(values))
        }).ToArray();

        if (target is Target.Respsody)
        {
            using var response = await client.Mget(expected.Select(pair => Key.Utf8(pair.Key)).ToArray());
            if (response.Length != expected.Length)
                throw new InvalidOperationException("MGET returned an unexpected number of values.");
            for (var index = 0; index < expected.Length; index++)
                if (!response[index].ToRespString().GetSpan().SequenceEqual(expected[index].Value))
                    throw new InvalidOperationException($"Unexpected value for {expected[index].Key}.");
        }
        else
        {
            var response = await db.StringGetAsync(expected.Select(pair => (RedisKey)pair.Key).ToArray());
            if (response.Length != expected.Length)
                throw new InvalidOperationException("MGET returned an unexpected number of values.");
            for (var index = 0; index < expected.Length; index++)
                if (!((ReadOnlyMemory<byte>)response[index]).Span.SequenceEqual(expected[index].Value))
                    throw new InvalidOperationException($"Unexpected value for {expected[index].Key}.");
        }
    }
}

