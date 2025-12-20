using Garnet;
using Garnet.server;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Respsody;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Resp;
using System.Diagnostics;
using System.Globalization;
using System.Net;

var ipEndPoint = new IPEndPoint(IPAddress.Loopback, 6909);

using var garnet = new GarnetServer(
    new GarnetServerOptions
    {
        EndPoints = [ipEndPoint],
        logger = new ConsoleLoggerProvider(
                new OptionsMonitor<ConsoleLoggerOptions>(
                    new OptionsFactory<ConsoleLoggerOptions>([], []),
                    [],
                    new OptionsCache<ConsoleLoggerOptions>()))
            .CreateLogger("garnet")
    });

garnet.Start();


var clientFactory = new RespClientFactory();
var client = await new RespClientFactory().Create(ipEndPoint);

var smallValues = Enumerable.Range(0, 1000).Select(i => CreatePair(i + 1)).ToArray();
var largeData = Enumerable.Range(1024 * 1024, 512).Select(CreatePair).ToArray();

var iteration = 1;
var timingsTotal = new List<TimeSpan>();

while (true)
{
    var sw = Stopwatch.StartNew();
    var swTotal = Stopwatch.StartNew();
    foreach (var (key, value) in smallValues)
    {
        var rKey = Key.Utf8(key);
        var rValue = Value.ByteArray(value);

        sw.Restart();
        await client.Set(rKey, rValue);
        var elapsedSet = sw.Elapsed;

        sw.Restart();
        using var get = await client.Get(rKey);
        var elapsedGet = sw.Elapsed;

        Console.WriteLine($"{key,10} | SET {elapsedSet} | GET {elapsedGet}");
    }

    var values = largeData.Take((iteration % 25 + 1) * 25).ToArray();

    timingsTotal.Add(swTotal.Elapsed);
    Console.WriteLine("Total time get/setting small values:");
    Console.WriteLine($"{string.Join(Environment.NewLine, timingsTotal.Select((t,i) => $"{i+1}. {t}"))}");
    Console.Write($"Inserting large values: {new SizeInBytes(values.Sum(v => (long)v.Value.Length))}");
    await MSet(values);
    Console.WriteLine("... Done!");

    Console.WriteLine("Waiting for 5s before next iteration...");
    await Task.Delay(5000);

    iteration++;
}

return;


async Task MSet(KeyValuePair<string, byte[]>[] values)
{
    await client.Mset([.. values.Select(v => (Key.Utf8(v.Key), Value.ByteArray(v.Value)))]);
}

static KeyValuePair<string, byte[]> CreatePair(int size)
{
    return new KeyValuePair<string, byte[]>(size + "__k", Enumerable.Range(0, size).Select(b => (byte)b).ToArray());
}

[RespCommand("GET key", ResponseType.String)]
[RespCommand("SET key value:string", ResponseType.Void, MethodName = "SetStr")]
[RespCommand("SET key value", ResponseType.Void)]
[RespCommand("MGET key [key ...]", ResponseType.Array)]
[RespCommand("MSET key value [key value ...]", ResponseType.Void)]
public static class Commands;

public readonly struct SizeInBytes(long bytes)
{
    public override string ToString()
    {
        return bytes switch
        {
            >= 1024L * 1024 * 1024 =>
                $"{(bytes / (1024.0 * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture)} GiB",
            >= 1024L * 1024 => $"{(bytes / (1024.0 * 1024)).ToString("F2", CultureInfo.InvariantCulture)} MiB",
            >= 1024L => $"{(bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KiB",
            _ => $"{bytes} B"
        };
    }
}