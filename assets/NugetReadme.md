# Respsody

**Respsody** is an experimental, high-performance, asynchronous, general-purpose [RESP3](https://github.com/redis/redis-specifications/blob/master/protocol/RESP3.md) client library written in C#. It's currently in an early stage of development and intended for experimentation and community feedback.


## Quick start

```csharp
using Respsody;
using Respsody.Resp;
using System.Net;
using System.Text;
using static System.Console;

var clientFactory = new RespClientFactory();
using var client = await clientFactory.Create(new IPEndPoint(IPAddress.Loopback, 6379));

var utf8Key = Key.Utf8("respsody");
Key strKey = "gob_key"; // same as Key.Utf8
var arrKey = Key.ByteArray([1, 0, 1]);
var utf16Key = Key.Unicode("‼️");

await client.Set(utf8Key, Value.Utf8("hello, respsody!"));
await client.Set(arrKey, Value.ByteArray([0, 3, 0, 3, 6, 6]));
await client.Set(strKey, Value.Utf8("we need a gimmick!"));
await client.Set(utf16Key, Value.Unicode("⚡"));

//dispose non-void responses to release underlying buffers
using var getResponse = await client.Get(utf8Key);

WriteLine("- GET result -");
WriteLine(getResponse.ToString());

//for aggregate responses(arrays, maps, sets) dispose only root response
using var mgetResponse = await client.Mget([strKey, utf16Key, arrKey]);

OutputEncoding = Encoding.Unicode;
WriteLine("- MGET result -");
Write("strings: ");
Write(mgetResponse[0].ToRespString());
WriteLine(mgetResponse[1].ToRespString().AsUnicodeSpan());

Write("byte[]: ");
WriteLine($"{string.Join(",", [..mgetResponse[2].ToRespString().GetSpan()])}");

//define and generate commands:
[RespCommand("GET key", ResponseType.String)]
[RespCommand("SET key value", ResponseType.Void)]
[RespCommand("MGET key [key ...]", ResponseType.Array)]
[RespCommand("MSET key value [key value ...]", ResponseType.Void)]
[RespCommand("COMMAND DOCS [command-name:string [command-name:string ...]]", ResponseType.Map)]
public static class Commands;
```

Cluster usage:

```csharp
using Respsody.Cluster;

var clusterRouter = new ClusterRouter(
    new ClusterRouterOptions
    {
        SeedEndpoints = ["some_cluster_host1:6379", "some_cluster_host2:6379"],
        ClientOptions = new RespClientOptions()
    });

await clusterRouter.Initialize();

// execute command on primary node that is responsible for key
using var v1 = await clusterRouter.RouteTo(RolePreference.Primary).Get(Key.Utf8("some_key_1"));

// pick random node and execute COMMAND DOCS on it
using var docs = await clusterRouter.PickRandom().Command(COMMAND.DOCS);

//group objects by node and execute commands on it
(Key Key, Value Value)[] objects = [(Key.Utf8("k_1"), Value.Utf8("v_1")), /* ... */ (Key.Utf8("k_n"), Value.Utf8("v_n"))];
foreach (var (node, nodeObjects)in clusterRouter.RouteTo(RolePreference.Primary).GroupBy(objects, o => o.Key))
    await node.Mset(nodeObjects);

```

## Disclaimer

This project is an independent work and is not endorsed, supported, or certified by Redis.

## License

This project is licensed under the [MIT License](LICENSE).

