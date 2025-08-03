# Respsody

**Respsody** is an experimental, high-performance, asynchronous, general-purpose [RESP3](https://github.com/redis/redis-specifications/blob/master/protocol/RESP3.md) client library written in C#. It's currently in an early stage of development and intended for experimentation and community feedback.


## Quick start

```csharp
using Respsody;
using Respsody.Resp;
using System.Net;

var clientFactory = new RespClientFactory();
var client = await clientFactory.Create(new IPEndPoint(IPAddress.Loopback, 6379));

await client.Set(Key.Utf8("my_byte_array_key"), Value.ByteArray([0, 3, 0, 3, 6, 6]));
await client.Set(Key.Utf8("my_gob_key"), Value.Utf8("goblins are better than trolls."));

//**always** dispose non-void responses and do it exactly once
using var getResponse = await client.Get(Key.Utf8("my_byte_array_key"));

//for aggregate responses(arrays, maps, sets, pushes) dispose only root response
using var mgetResponse = await client.Mget([Key.Utf8("my_byte_array_key"), Key.Utf8("my_string_key")]);

var arr = mgetResponse[0].ToRespString().GetSpan().ToArray();
var str = mgetResponse[1].ToRespString().ToString(Encoding.UTF8);

//generate commands
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

