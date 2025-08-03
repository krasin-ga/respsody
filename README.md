# Respsody

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE) [![respsody Nuget](https://img.shields.io/nuget/v/Respsody?&label=Respsody)](https://www.nuget.org/packages/Respsody/)

<img alt="Respsody" src="assets/respsody_logo.svg" align="right" /> **Respsody** is an experimental, high-performance, asynchronous, general-purpose [RESP3](https://github.com/redis/redis-specifications/blob/master/protocol/RESP3.md) client library written in C#. It's currently in an early stage of development and intended for experimentation and community feedback.

 ⚠️ This library is **not production-ready** at the moment. Expect breaking changes, missing features, and limited error handling. 


## Features

- Compatibility with any RESP3-compliant server, including Redis, Garnet, Dragonfly, Valkey, and others
- Code generation for arbitrary commands
- Efficient memory usage through scoped buffer reuse
- Full Redis Cluster protocol support with advanced routing: primary/replica node targeting, slot-aware command dispatch, and optimized MGETs by grouping keys per responsible node


## Installation

```
dotnet add package respsody --prerelease
```

## Benchmarks
Tested against the StackExchange.Redis client. Click on the spoilers to reveal the results.

>Hardware: AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores

>RESP Server: [Garnet](https://github.com/microsoft/garnet) was run in a separate process on localhost

### BenchmarkDotNet

<details>
<summary>
Windows 11, .NET 9.0.5 <br>

</summary>

| Method                                                                | Mean       | Op/s        | Gen0   | Gen1   | Gen2   | Allocated |
|---------------------------------------------------------------------- |-----------:|------------:|-------:|-------:|-------:|----------:|
| '[when_all 12_500 tasks] [Respsody] Get (noop) commands/s'            |   614.5 ns | 1,627,287.0 | 0.0200 | 0.0125 |      - |     184 B |
| '[when_all 12_500 tasks] [StackExchange.Redis] Get (noop) commands/s' |   981.8 ns | 1,018,550.5 | 0.0488 | 0.0325 |      - |     416 B |
| '[when_all 12_500 tasks] [Respsody] Get (json) commands/s'            |   913.7 ns | 1,094,486.0 | 0.1288 | 0.0613 | 0.0025 |    1041 B |
| '[when_all 12_500 tasks] [StackExchange.Redis] Get (json) commands/s' | 1,512.3 ns |   661,241.9 | 0.1800 | 0.0675 | 0.0125 |    1464 B |


| Method                | MessageSize | Mean     | Op/s    | Gen0   | Gen1   | Allocated |
|---------------------- |------------ |---------:|--------:|-------:|-------:|----------:|
| Respsody_Set_Get      | 256         | 183.4 us | 5,453.9 |      - |      - |     353 B |
| SE_Set_Get            | 256         | 224.8 us | 4,449.0 |      - |      - |    1064 B |
| Respsody_MSet_MGet_10 | 256         | 186.9 us | 5,350.5 |      - |      - |    1570 B |
| SE_MSet_MGet_10       | 256         | 219.2 us | 4,561.5 |      - |      - |    5192 B |
| Respsody_Set_Get      | 32786       | 198.7 us | 5,032.4 |      - |      - |     377 B |
| SE_Set_Get            | 32786       | 269.0 us | 3,718.1 | 0.4883 |      - |   33601 B |
| Respsody_MSet_MGet_10 | 32786       | 530.5 us | 1,885.0 |      - |      - |    1810 B |
| SE_MSet_MGet_10       | 32786       | 608.7 us | 1,642.8 | 5.8594 | 1.9531 |  330554 B |

*SE -> StackExchange.Redis*
</details>


<details >
<summary>Ubuntu 20 LTS (Focal Fossa) *WSL Virtualization*, .NET 9.0.5</summary>

| Method                                                                | Mean       | Op/s        | Gen0   | Gen1   | Gen2   | Allocated |
|---------------------------------------------------------------------- |-----------:|------------:|-------:|-------:|-------:|----------:|
| '[when_all 12_500 tasks] [Respsody] Get (noop) commands/s'            |   736.9 ns | 1,357,001.3 | 0.0200 | 0.0100 |      - |     184 B |
| '[when_all 12_500 tasks] [StackExchange.Redis] Get (noop) commands/s' | 1,283.0 ns |   779,449.1 | 0.0475 | 0.0325 |      - |     416 B |
| '[when_all 12_500 tasks] [Respsody] Get (json) commands/s'            | 1,148.7 ns |   870,574.8 | 0.1288 | 0.0563 | 0.0025 |    1042 B |
| '[when_all 12_500 tasks] [StackExchange.Redis] Get (json) commands/s' | 1,737.4 ns |   575,570.6 | 0.1825 | 0.0950 | 0.0200 |    1464 B |
</details>

#### Non-scientific
These benchmarks run a series of test scenarios and measure total metrics.

<details>
<summary>
Windows 11, .NET 9.0.5 <br>

</summary>


| Scenario                              | Target             | Dataset                    | Iterations | TotalElapsed |  BestRun |  CpuTime | TotalAllocated |
|---------------------------------------|--------------------|----------------------------|------------|--------------|----------|----------|----------------|
| SetGet_Sequential                     | Respsody           | small_20K 20000KV 2.45 MiB |         50 |      157.19s |    2.97s |  179.31s |      46.77 MiB |
| SetGet_Sequential                     | StackOverflowRedis | small_20K 20000KV 2.45 MiB |         50 |      209.82s |    3.82s |  218.55s |     667.54 MiB |
| -                                     | -                  | -                          |          - |            - |        - |        - |              - |
| SetGet_Sequential                     | Respsody           | large_300 300KV 151.62 MiB |         50 |       13.61s | 184.48ms |    8.78s |       9.69 MiB |
| SetGet_Sequential                     | StackOverflowRedis | large_300 300KV 151.62 MiB |         50 |       24.22s | 372.82ms |   14.38s |       7.42 GiB |
| -                                     | -                  | -                          |          - |            - |        - |        - |              - |
| SetGet_Sequential_WithJsonDeserialize | Respsody           | jsons_10K 10000KV 2.25 MiB |         50 |       90.31s |    1.73s |  107.67s |     461.92 MiB |
| SetGet_Sequential_WithJsonDeserialize | StackOverflowRedis | jsons_10K 10000KV 2.25 MiB |         50 |      128.20s |    2.16s |  128.30s |       1.04 GiB |
| -                                     | -                  | -                          |          - |            - |        - |        - |              - |
| SetGet_WhenEach                       | Respsody           | small_20K 20000KV 2.45 MiB |         50 |        1.47s |  24.02ms |    9.09s |     316.49 MiB |
| SetGet_WhenEach                       | StackOverflowRedis | small_20K 20000KV 2.45 MiB |         50 |        2.38s |  34.12ms |   15.62s |     852.86 MiB |
| -                                     | -                  | -                          |          - |            - |        - |        - |              - |
| OneSetTwoGets_WhenEach                | Respsody           | small_20K 20000KV 2.45 MiB |         50 |        1.74s |  30.78ms |   16.52s |     387.12 MiB |
| OneSetTwoGets_WhenEach                | StackOverflowRedis | small_20K 20000KV 2.45 MiB |         50 |        3.29s |  52.22ms |   21.67s |       1.24 GiB |
| -                                     | -                  | -                          |          - |            - |        - |        - |              - |
| SetGet_WhenEach_WithJsonDeserialize   | Respsody           | jsons_10K 10000KV 2.25 MiB |         50 |     763.14ms |  13.88ms |    4.33s |     597.01 MiB |
| SetGet_WhenEach_WithJsonDeserialize   | StackOverflowRedis | jsons_10K 10000KV 2.25 MiB |         50 |        1.24s |  20.73ms |    7.42s |       1.12 GiB |
| -                                     | -                  | -                          |          - |            - |        - |        - |              - |
| OneSetTwoGets_WhenEach                | Respsody           | large_300 300KV 151.62 MiB |         50 |       18.14s | 280.61ms |   12.23s |      74.49 MiB |
| OneSetTwoGets_WhenEach                | StackOverflowRedis | large_300 300KV 151.62 MiB |         50 |       23.55s | 339.96ms |   22.36s |      15.23 GiB |
| -                                     | -                  | -                          |          - |            - |        - |        - |              - |
| MSetGet                               | Respsody           | small_20K 20000KV 2.45 MiB |         50 |     649.52ms |   8.35ms | 890.62ms |     157.21 KiB |
| MSetGet                               | StackOverflowRedis | small_20K 20000KV 2.45 MiB |         50 |        1.07s |  16.51ms |    1.78s |     241.06 MiB |
| -                                     | -                  | -                          |          - |            - |        - |        - |              - |
| MSetGet                               | Respsody           | large_300 300KV 151.62 MiB |         50 |        9.27s | 165.54ms |    5.00s |      11.91 MiB |
| MSetGet                               | StackOverflowRedis | large_300 300KV 151.62 MiB |         50 |       22.07s | 297.64ms |   43.58s |      27.06 GiB |
</details>

## Quick Start

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

## Known Limitations

* Pub/Sub API is limited to the RESP3 variant
* No support for secure connections
* No specialized API for selecting logical databases
* No flow control/backpressure — use an external rate limiter
* No support for Sentinel
* No dedicated API for transactions. They can be made with command combos, though the approach is a bit hacky:
```
    using var comboResult = await Combo.Build(client)
        .Cmd(r => r.MultiCommand())
        .Cmd(r => r.IncrVoidCommand(key)) // Void overload used to avoid processing the -QUEUED response
        .Cmd(r => r.ExecCommand())
        .Execute();
```

## Contributing

Contributions, bug reports, and feedback are welcome and appreciated.

## Disclaimer

This project is an independent work and is not endorsed, supported, or certified by Redis.

## License

This project is licensed under the [MIT License](LICENSE).

