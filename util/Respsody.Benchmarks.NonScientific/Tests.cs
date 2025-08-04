using System.Runtime.CompilerServices;
using System.Text.Json;
using Respsody.Benchmarks.NonScientific.Library;
using Respsody.Client;
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

            return 0;
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
                i += JsonSerializer.Deserialize<SampleJson>(val.GetSpan())!.VectorValue.Length;
            }
            return (int)i;
        }

        var j = 0L;
        var (seValues, _) = DataCache.AsSe(values);
        foreach (var (key, value) in seValues)
        {
            await db.StringSetAsync(key, value);
            var res = await db.StringGetAsync(key);
            j += JsonSerializer.Deserialize<SampleJson>(res!)!.VectorValue.Length;
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

                                       i2 += JsonSerializer.Deserialize<SampleJson>(val.GetSpan())!.VectorValue.Length;
                                   })))
            {
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
                                   j2 += JsonSerializer.Deserialize<SampleJson>((await db.StringGetAsync(v.Key))!)!.VectorValue.Length;
                               })))
        {
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
                                       l += (int)seValue.Length();
                                   })))
            {
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
                                   await db.StringGetAsync(v.Key);
                               })))
        {
            j++;
        }

        return j;
    }
}