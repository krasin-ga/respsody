# AGENTS.md

## Project Goal

Respsody aims to be the most performant RESP/Redis client, with the lowest possible overhead. Treat that as an engineering target to demonstrate with reproducible measurements, not an established ranking or a promise that every path is allocation-free.

- Preserve correct RESP semantics, command ordering, buffer ownership, and completion behavior. Dropped work, leaked buffers, or incomplete sends invalidate performance results.
- Prioritize CPU cost per command, allocations and GC pressure, bytes copied, synchronization, socket operations, throughput, and latency under load. Include retained pool memory and cold-start costs where relevant.
- Preserve source-generated command construction, pooled buffers and completion sources, span-based access, and batching where they help. Measure before replacing them with general-purpose abstractions.
- Avoid adding locks, atomics, defensive copies, closures, per-command tasks, or repeated validation to hot paths without a concrete requirement and evidence. Necessary correctness fixes still take priority; compare implementations and keep validation at appropriate boundaries.
- Distinguish connection setup, compile-time generation, response materialization, and steady-state execution when assessing costs. Pooling trades allocation frequency for retained memory.
- For performance changes, compare equivalent workloads before and after in Release with the same runtime, server, payloads, concurrency, configuration, and response consumption. Report throughput together with CPU, allocation, and latency measurements.

## Build & Test

The library and tests target .NET 8; utility projects target .NET 10. Use a .NET 10 SDK for the complete solution and install the .NET 8 runtime to run the tests. There is no `global.json` pinning the SDK.

```bash
dotnet build Respsody.slnx
dotnet test test/Respsody.Tests
```

No separate lint, typecheck, or formatter commands are configured. `dotnet build` covers compilation and type checking. All utility projects are included in the solution build.

### Test prerequisites

| Tests | Requirements |
| --- | --- |
| `FramingTest`, `RespFramingTest` | In-memory framing and aggregation tests; no server or Docker. |
| `ServerTests` | Embedded Garnet and local TCP sockets; no Docker. Includes a 128 MiB payload case. |
| Focused regression, generator, and cluster loopback tests | No Docker; some open ephemeral local TCP listeners. |
| `ReliabilityTests`, `PubSubTest` | Running Docker engine; Testcontainers starts isolated Redis 7.4.7 containers with dynamic host ports. |
| `RedisClusterTests` | Running Docker engine; Testcontainers starts nine Redis 6.2 nodes with dynamic host ports. |

Container fixtures use generated names and dynamic host ports. The cluster fixture translates advertised internal ports to their mapped host endpoints. The pub/sub fixture resolves the current mapped endpoint on reconnect because Docker can change a random port after restart. Fixtures dispose containers and networks, including failed setup. Report results from actual runs; no test group is guaranteed to pass.

```bash
dotnet test test/Respsody.Tests --filter "FullyQualifiedName~FramingTest"
dotnet test test/Respsody.Tests --filter "FullyQualifiedName~ServerTests"
dotnet test test/Respsody.Tests --filter "FullyQualifiedName~ReliabilityTests"
dotnet test test/Respsody.Tests --filter "FullyQualifiedName~PubSubTest"
```

The first filter includes both framing test classes. The full test command also runs Docker-dependent tests. `ReliabilityTests` runs 100 iterations of 10,000 concurrent scenarios, each including the nested map from `COMMAND DOCS GET`. This bounds response size while preserving repeated aggregate-disposal coverage. There are no test traits separating these groups today.

## Solution Structure

```text
src/Respsody/                         Main library (net8.0, unsafe enabled)
src/Respsody.Generators/               Roslyn incremental generator (netstandard2.0)
test/Respsody.Tests/                   xUnit tests (net8.0)
util/Respsody.Benchmarks/              BenchmarkDotNet harness (net10.0)
util/Respsody.Benchmarks.NonScientific/ Manual scenario harness (net10.0)
util/Respsody.Util.Playground/         Experiments (net10.0)
```

There are no checked-in CI workflows. Version is defined in `Directory.Build.props` under `<Version>`.

## Source Generator

Entry point: `src/Respsody.Generators/CommandsSourceGenerator.cs` (`IIncrementalGenerator`).

Flow: `[RespCommand("SET key value", ResponseType.Void)]` on a static class -> `RawSyntaxParser` tokens -> `CommandSyntaxParser` AST -> `CommandNodeExplication` variations -> `Build` emits Roslyn syntax trees. Generated extension methods target `IRespClient`, including implementations of its derived interface `IDirectRespClient`; the generator also emits command builders and option types.

`Respsody` references the generator project as an ordinary project reference. Tests and utilities explicitly reference it with `OutputItemType="Analyzer"` to run it on their command declarations. Do not assume the library reference alone registers the generator as an analyzer in another project.

`EmitCompilerGeneratedFiles` and the `Generated` output path are set on the generator project itself. That setting does not automatically persist the output it generates in consuming projects. To inspect that output, enable `EmitCompilerGeneratedFiles` on the consumer and set `CompilerGeneratedFilesOutputPath` under its `obj/` directory; do not edit generated files.

The generator uses `LangVersion=latest` but targets `netstandard2.0`. Language syntax and target API availability are separate constraints: check available references before using APIs such as spans or `Memory<T>`, and preserve compiler-host compatibility.

Generated command builders construct unsent commands and have no cancellation token. Older explicit-token builder overloads remain as deprecated compatibility wrappers; pass cancellation to execution or Combo instead. Generator regression tests compile representative declarations and verify emitted wire bytes, aliases, diagnostics, and culture-independent APIs.

## Architecture & Ownership

- **Network:** `StructuredSocket<TIncoming, TPayload>` coordinates asynchronous connect, send, receive, and disconnected expiration loops. Connected sends use an unbounded `Channel<Outgoing>` with one reader. Small commands are copied into a coalescing buffer; large ones use scatter/gather sends.
- **Memory:** `MemoryBlocks` attaches ownership to leased `MemoryBlock` objects and delegates storage to `StandardMemoryBlocksPool` or `NoOpPool`. `OutgoingBuffer` tracks command-owner and delivery-pipeline leases. `ThreadStaticPool<T>` exchanges object batches between per-thread storage and shared queues.
- **RESP:** `RespFraming` identifies frames. A frame contained in one block can borrow it; a frame crossing blocks is copied into an `ArrayPool<byte>` rental. `RespFrameAggregationStrategy` assembles arrays, maps, sets, attributes, pushes, and streamed strings.
- **Client:** `RespClient` matches replies to queued payloads and completes pooled `PooledValueTaskCompletionSource<T>` instances. `CompletionGuard` arbitrates completion and guards response disposal. Pooling does not eliminate all allocations: a payload creates a completion guard, and connection buffers, pool growth, and materialization also have costs.
- **Cluster:** `ClusterRouter` routes by CRC16 hash slot, handles MOVED/ASK replies, and supports primary/replica preferences. Key encoding and hash tags affect slot selection.

Buffer-backed responses are structs with shared ownership state. Dispose the root response; children of aggregates borrow its lifetime. Copying a response struct does not copy its bytes or create independent ownership. Spans and other borrowed views must not outlive the root. Concurrent use/disposal of the same response is not established as a supported ownership pattern. Consume each returned `ValueTask` once so its pooled source can be returned by `GetResult`.

`Key` and `Value` support string encodings and byte memory. Generated execution methods manage command disposal; manually created commands require explicit ownership management. `Combo` queues a group of commands as one channel item, avoiding per-command response waits. It does not guarantee one socket send, one packet, or transaction semantics. Fluent builders consume all member outcomes and release successful response lifetimes if a member fails; low-level callers manage their futures explicitly.

See [diagram.md](docs/diagram.md) for detailed execution and memory flows.

## Benchmarks

```bash
dotnet run -c Release --project util/Respsody.Benchmarks
dotnet run -c Release --project util/Respsody.Benchmarks.NonScientific
```

Inspect each `Program.cs` before running: scenarios and settings are selected in code. The BenchmarkDotNet entry point currently runs `SequentialBenchmark` and starts embedded Garnet on loopback port 6399; other benchmark classes are not selected automatically. The manual harness expects an already running server on loopback port 6379. The README tables describe historical .NET 9.0.5 runs; current utility projects target .NET 10, so those tables are not current-checkout validation.

Use the manual harness for exploration and inspect its measurement limitations in [ca_review.md](ca_review.md). For comparisons, record commit, hardware, OS, runtime, client/server versions, GC configuration, payload size, concurrency, and operation definition. Separate borrowed-buffer consumption from string/array/JSON materialization. Report latency percentiles separately from throughput-derived time per operation.

## Conventions

- Main library and tests use .NET 8 / C# 12 defaults; nullable reference types are enabled throughout. Unsafe code is enabled in the main library.
- Do not add comments to production code; follow the code-only convention for new changes.
- Use xUnit `[Theory]`/`[Fact]` and fixtures where appropriate; use `ITestOutputHelper` for test diagnostics.
- Existing integration tests use fixed delays and polling. Prefer observable completion with bounded waits for new lifecycle tests; there is no `[TimeStamped]` test convention in this tree.
- Add focused regressions for demonstrated correctness bugs. Performance tests should validate equivalent work and ownership, not merely count completed calls.

## Review Status

[ca_review.md](ca_review.md) records resolved findings, validation evidence, measurement limits, and the historical diagnosis at `433d741`. Consult its resolution table before treating a historical finding as an open defect. Neither a review nor passing tests establish exhaustive correctness or a fastest-client ranking.
