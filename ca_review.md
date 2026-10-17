# Respsody Code Review

## Scope and performance objective

The original diagnosis was validated against `433d741` on 2026-10-17. The findings have since been addressed in individual issue commits. The resolution record below describes the changed implementation; the historical sections preserve the evidence and withdrawn claims that motivated that work. Historical source descriptions are not statements that those bugs remain open.

The goal is to make Respsody the most performant RESP/Redis client with the lowest overhead. Correct results, complete writes, command ordering, and reliable buffer reuse are prerequisites for meaningful performance numbers. Prefer targeted fixes that preserve generated serialization, pooled ownership, borrowed responses, and batching. Avoid prescribing hot-path locks, atomics, allocations, or copies without a demonstrated need and a measured cost.

The implementation minimizes allocations, but is not universally allocation-free: each ordinary command payload creates a `CompletionGuard`, fragmented frames may require copies and lease wrappers, and pools allocate when initialized or expanded. See [the architecture and ownership guide](docs/diagram.md).

**Historical evidence labels:** “reproduced” means a small temporary .NET 8 probe against the baseline library demonstrated the result; “source-confirmed” means a concrete failing path was traced. The fixes now include checked-in regression tests. This review is not exhaustive.

## Resolution record

Each hash identifies an issue-specific commit on the working branch. Several original findings grouped independent defects and therefore have several commits.

| Finding | Implemented behavior | Commits |
| --- | --- | --- |
| R1: partial writes / stalled sends | Contiguous and scatter/gather paths send the remaining bytes; connection cancellation closes blocked sockets. Forwarding methods return ValueTasks directly without extra async wrappers. | `6d90f9a`, `25a3d27`, `6a4e921` |
| R2: disconnected queue handling | Retain live batches; retain FIFO order in a private pending queue; stop and await expiration after failed initialization. | `611b39b`, `ddff3b3`, `15e6714` |
| R3: integer range | Span-based signed 64-bit parsing checks complete consumption and overflow. | `a34d82e` |
| R4: retention accounting | Roll back the rejected block reservation only. | `7de408a` |
| R5: pool capacity | Allocate the full bucket capacity; reused blocks satisfy requests. | `c5c0ef0` |
| R6: long-key encoding | Keep and return the original rental; hash/serialize its written span. | `4d59a41` |
| R7: reset ownership | Reset CR/LF and length parser state, release framing tails/current leases, and dispose partial aggregation state. | `ed5d2a7`, `a2a51b3`, `54dade9` |
| R8: late scalar response | Release the frame and attribute after completion was already claimed. | `1b48f59` |
| R9: Combo | Pack fifth command once, infer factory overloads, release command owners, and consume every future on fluent success/failure. | `275b5c7`, `ca7136c`, `a1fbc70` |
| R10: cluster clients | Preserve reusable client instances, dispose failed probes/replacements, coordinate refresh publication with shutdown, and release the pre-initialization refresh gate. | `b5a8fde`, `704c95d`, `2df5226` |
| R11: redirect budgets | Charge complete elapsed time and reject exhausted budgets; consume ASKING completion and propagate setup errors, including unpublished preparation/submission failures. Regression traffic counts exclude topology refresh. | `77eba16`, `d2194cf`, `563b390`, `d313c7c` |
| R12: routing boundaries | Use unsigned indexing across wraparound and explicitly report uncovered slots. Random role routing selects primary families. | `a7a84e6`, `b2f702a`, `6992706` |
| R13: delivery ownership | Balance unsent disposal, lazy startup, rejected submission, queued shutdown, failed batch tails, and unpublished preparation failures. | `40a0a26`, `d75bddf`, `fde50a8`, `e73080a`, `3311ddd` |
| Generator equality | Compare ordered explications and use order-sensitive hashing. | `806cec6` |
| Generator literals | Escape ordinary and UTF-8 source literals with Roslyn; test compiled wire bytes. | `482f4de` |
| Generator discovery / culture | Match exact attribute symbols including aliases; use invariant identifier transformations. | `f8c9066`, `2e42108` |
| Generator diagnostics / cancellation API | Diagnose empty definitions and unfinished literals; emit token-free command builders with deprecated explicit-token compatibility overloads. Execution still forwards cancellation. | `bc8c37a`, `dc44019`, `7f5243a` |
| Standalone block finalization | Permit blocks without a pool owner to finalize safely. | `f339db1` |
| Scalar validation / borrowed lifetime | Reject malformed big numbers, booleans, and special doubles; check disposal before accessing pooled response data. | `03d06cf`, `2445397`, `1a0b3ce`, `c52a08e` |
| Materialization / preencoding | Size maps by pair count; encode static bulk strings directly into their final byte array; compare protocol tokens ordinally. | `830c541`, `766f64d`, `b797a23` |
| Additional aggregation defects | Skip array headers in enumeration, release streamed-string terminators, and exclude collection end markers. | `59c48f3`, `d7332d2`, `d39a0fd` |
| Startup / initialization resources | Coordinate Start/Dispose on the cold path, clean DirectSocketRpc resources, close failed-handshake sockets, and release attributes after errors/disconnect. | `d2ac34c`, `8623fa1`, `7c4f445`, `2d52b4a` |
| Throwing diagnostics callbacks | Finish queue/batch/Combo cleanup, fault a claimed response if its execution callback throws, and continue expiration scanning after callback failures. | `7a00beb`, `dfc5522` |
| Subscription cancellation / ACK ordering | Honor caller cancellation/timeout until all acknowledgements arrive; retain ordering across late ACKs, errors, disconnect, and repeated channel arguments. | `968314e`, `af71617` |
| Argument-free unsubscribe | Track subscription families lazily from ACKs, snapshot after preceding subscribe ACKs, distinguish null from empty channels, and reset membership on disconnect. | `9c151e3` |
| Idle send-wait allocations | Wake the noncancelable pooled channel waiter through one send-loop lifetime cancellation registration; keep the socket-close cancellation path. | `e0ae79f` |
| Malformed topology | Validate node entries, slot boundaries, and conflicting primary assignments at refresh. | `182b638` |
| RESP error observers / MOVED routing | Invoke configured observers before error completion, isolate observer failures, and let MOVED update routing without waiting for the periodic refresh. | `8572770` |
| Benchmark CPU / concurrent accounting | Remove CPU spinning; aggregate per-worker results outside measured work. | `eb23422`, `93746dc` |
| Benchmark equivalence / cleanup | Validate results outside timing and dispose both clients and the JSON writer. | `864d6e5`, `e4e7b88`, `e5fdc88` |
| Buffer benchmarks | Acquire the shared lock correctly; bound stack allocation and expose size cases. | `0e547a6`, `9a859c1` |
| Docker / stress coverage | Use isolated names/dynamic ports, clean fixture resources, dispose the stress client, and bound repeated DOCS responses to GET while keeping all million scenarios. | `fcbd553`, `a54621d`, `f52d974`, `e8993fc` |
| Hash-slot coverage | Add known Redis vectors, empty/nested braces, UTF-8, and binary tags. | `361927b` |
| Pub/sub test race | Transfer received messages through a channel and await bounded message/reconnect events instead of enumerating concurrently mutated lists or relying on delays. Count successful connections rather than failed attempts when waiting for restart. | `d01b2bc`, `e0a3292` |

### Validation after fixes (2026-10-17)

- `dotnet build Respsody.slnx -c Release`: passes. Remaining baseline warnings are RS2008 analyzer release tracking and CS0162 unreachable playground code; obsolete Testcontainers constructors were updated during fixture work.
- `dotnet test test/Respsody.Tests -c Release --no-build`: **317 passed, 0 failed, 0 skipped**, including Docker integration and the full million-scenario stress test (1m09s). An earlier combined run exposed two test assumptions around topology-refresh traffic and first successful reconnect; both were corrected before this pass.
- The three Docker test classes passed together, including all 100 x 10,000 reliability scenarios, pub/sub reconnect, and the nine-node cluster. Fixtures left no Redis containers behind.
- Focused suites cover numeric boundaries, pool accounting/capacity, long-key serialization, parser resets, framing/aggregate leases, late replies, startup/disposal, blocked sends, batch ordering, generator compilation, and cluster lifecycle/redirection. Loopback send tests verify exact bytes for large contiguous and segmented writes with small socket buffers; partial-return behavior is still OS-dependent. They are supplemented by a stalled-reader shutdown test, not a claim to simulate every kernel send schedule.
- Both benchmark utility projects build in Release. An external smoke harness checked all 16 manual scenario/client combinations, BenchmarkDotNet setup/validation/cleanup, buffer contention, CPU reads, and JSON serialization. Full comparative throughput/latency benchmarks have not been rerun.

### Measured costs and remaining limits

A temporary Release microprobe compiled copies of the baseline parser/key methods from `433d741` beside their fixed equivalents, using .NET 8.0.24 on Windows 26200 x64, AMD Ryzen 7 7840S (16 logical CPUs), workstation GC. It parsed `:123456789\r\n` ten million times and hashed a 512-byte ASCII UTF-8 key 250,000 times per round. Values are medians of seven alternating rounds in the same process, without a server; they are amortized micro-operation costs, not request latency or a comparison of complete revision builds.

| Probe | Before | After |
| --- | --- | --- |
| Integer parsing within the old parser's valid 32-bit range | 7.50 ns/op, 0 B/op | 11.26 ns/op, 0 B/op |
| 512-byte UTF-8 key hash | 1411.70 ns/op, 536 B/op | 1397.22 ns/op, 0 B/op |

The integer parser now validates full signed 64-bit input at a measured cost of about 3.76 ns in that narrow case. The long-key fix removes a copy/allocation and returns the rental correctly. A 65-byte pool request now receives a 128-byte bucket: 63 additional capacity bytes in exchange for safe reuse, bounded by corrected retention accounting. These results do not establish whole-client performance improvement across workloads.

Median process CPU was 7.81 to 10.94 ns/parser operation and 1375 to 1375 ns/key operation. The pool retention budget covers idle blocks stored in the pool; it does not cap outstanding leases, thread-local blocks, or total process memory.

A second external probe compared complete Release builds of `433d741` and `e0ae79f` against Redis 7.4.7 in a separate Docker Desktop Linux container, persistence disabled. It used the same Windows/CPU/.NET 8 environment, workstation GC, one connection, default client settings, generated commands, distinct keys, and 256-byte binary values. Each fresh process warmed 1,000 iterations, forced collection before measuring, validated every returned byte, and consumed/disposed every future/response. Three rounds alternated revision order; heavy builds/tests were paused during the final measurement.

| Workload | Redis commands/s, before → after | Client CPU, before → after | Allocated bytes per SET+GET pair, before → after |
| --- | --- | --- | --- |
| 10,000 sequential pairs | 1,536 → 1,557 | 2,453 → 2,297 ms | 52.92 → 52.97 |
| 64,000 pairs in batches of 32 pairs (64 queued commands) | 68,209 → 67,443 | 672 → 547 ms | 48.67 → 48.74 |

These are three-round medians. Sequential pair p99 was 1.979 to 1.816 ms; whole-pipeline-batch p99 was 1.718 to 1.620 ms, not individual request latency. No measured GC collections occurred. Throughput was roughly similar (sequential +1.35%, pipeline -1.12%), and CPU varied enough that the lower observations are not proof of a stable gain. Docker Desktop RTT dominates the sequential case; this small diagnostic does not cover large payloads, cluster load, saturation, retained memory, or server CPU.

The first integrated probe exposed about 261 B/pair in sequential traffic from cancellable channel waiters. `e0ae79f` restored approximately baseline allocation while keeping idle and blocked-send cancellation correct. The external source, reproduction instructions, and raw results are retained in `D:/repos/respsody-worktrees/e2e-probe`; they are not checked-in benchmark artifacts or a fastest-client comparison.

The manual harness still uses process-wide measurements, fixed client order, shared caches/pools, and forced-GC behavior. README tables remain historical .NET 9.0.5 results, while current utilities target .NET 10. Record fresh equivalent-workload measurements before claiming a fastest-client ranking. Concurrent disposal of the same response remains outside the supported single-owner lifetime contract; no atomic was added to every response disposal. Thread-local pool initialization and retained-memory tradeoffs remain measurement targets, not unresolved correctness findings.

## Historical baseline validation

- `dotnet build Respsody.slnx`: succeeded with four warnings: RS2008 analyzer release tracking, CS0162 unreachable playground code, and two CS0618 Testcontainers constructor warnings.
- `dotnet test test/Respsody.Tests --no-build --filter "FullyQualifiedName~FramingTest"`: 28 passed.
- `dotnet test test/Respsody.Tests --no-build --filter "FullyQualifiedName~ServerTests"`: 5 passed, using embedded Garnet without Docker.
- Full suite with Docker available: 35 passed, 1 failed during fixture setup. `ReliabilityTests` collided with `PubSubTest` while creating the fixed `redis-single-node-net` network. The cluster and pub/sub tests passed.
- The isolated 100-iteration reliability stress run was stopped before completion; it includes one million `COMMAND DOCS` calls. A temporary external harness then invoked the actual `ShouldBeProtectedAgainstMultipleDisposals(1)` test body: 10,000 scenarios passed in 89.1 seconds, with fixture setup/cleanup completing successfully (92.4 seconds total, exit 0). This does not establish a pass for the full stress workload.
- No performance benchmarks were run. Existing README measurements were not reproduced.

These baseline results did not cover the boundary and lifecycle defects described below. Consult the resolution record above for their current status.

## Historical runtime diagnosis

These findings describe `433d741`, ordered roughly by impact on correctness and ownership. Source links now open the corrected files; descriptions and line-specific observations refer to the baseline.

### R1. Socket writes assume every byte was sent

**Source-confirmed; fix before trusting sustained-load results.** Both [`StructuredSocket.SendInternal` overloads](src/Respsody/Network/StructuredSocket.cs#L230) discard the byte count returned by `Socket.SendAsync`; [`DirectSocketRpc.Rpc`](src/Respsody/Client/DirectSocketRpc.cs#L51) does likewise during initialization. A partial send leaves a truncated RESP command while the caller advances or releases the entire buffer.

Track progress and send the remainder for both contiguous and scatter/gather buffers. Release delivery ownership only after the complete write or failure cleanup. Test forced partial writes and stalled peers, not just large payloads over a fast loopback connection.

The scatter/gather overload also lacks cancellation. [Reconnection waits for both loops](src/Respsody/Network/StructuredSocket.cs#L109) before leaving the socket's disposal scope, so a blocked send to a peer that stops reading can stall teardown. Resolve this at connection lifetime boundaries without adding a cancellation registration per normal command.

### R2. Disconnected expiration loses live batches and can reorder singles

**Source-confirmed.** In [`HeartbeatLoop`](src/Respsody/Network/StructuredSocket.cs#L289), a batch whose first payload has not expired is removed from the channel and skipped without being added to `alive`. A Combo queued while connecting/reconnecting can disappear with its futures and delivery leases outstanding.

Singles are retained, but a producer can enqueue a newer item after the drain and before older items are requeued, placing the new item first. This occurs in the disconnected expiration path; on successful initialization, the heartbeat is stopped before the established send loop takes over. Preserve live batches and FIFO ordering, with deterministic tests around the drain/requeue boundary.

An exception from `InitializeConnection` also skips the heartbeat cancellation/await below it. Disposing its token source does not cancel that loop, so an orphan reader can survive into another attempt despite the channel's `SingleReader` setting. Put heartbeat shutdown on every connection-attempt exit path and test initialization failure.

### R3. RESP integers are accumulated as 32-bit values

**Reproduced.** [`RespNumber.ToInt64`](src/Respsody/Resp/RespNumber.cs#L21) uses an `int` accumulator and performs the final multiplication as `int`. Input `:2147483648\r\n` returns `-2147483648`.

Use a correct signed 64-bit parser and test both 32-bit boundaries, `long.MinValue`, `long.MaxValue`, malformed digits, and overflow. Merely changing the accumulator type is not a complete checked-overflow design for the negative boundary. Keep parsing on spans and measure the valid-input path.

### R4. Pool accounting loses already retained bytes

**Reproduced without concurrency.** [`StandardMemoryBlocksPool.Return`](src/Respsody/Memory/StandardMemoryBlocksPool.cs#L75) rejects a block at the limit by subtracting the whole accumulated counter instead of the rejected block's size.

With a 128-byte limit, return two outstanding 64-byte blocks: one remains stored, but the counter becomes zero. Renting that stored block then makes the counter -64. Concurrent calls can worsen the discrepancy. This allows retention beyond the configured budget; the earlier claim that the limit becomes “permanently ineffective” was too absolute.

Maintain accounting for actual retained blocks and roll back only the rejected reservation. Verify boundary and concurrent lease/return behavior without introducing a global lock by default.

### R5. A pool bucket can return a block smaller than requested

**Reproduced.** [Pool lookup rounds sizes to a power-of-two bucket](src/Respsody/Memory/StandardMemoryBlocksPool.cs#L34), while [allocation uses the exact requested size](src/Respsody/Memory/StandardMemoryBlocksPool.cs#L19). Lease and return 65 bytes, then request 128: the returned block has `Size == 65`.

Use consistent bucket capacities or ensure that reused blocks satisfy the requested capacity. Test non-power-of-two requests and buffer growth, and measure the retained-memory tradeoff of rounding.

### R6. Long-key encoding loses the rented array and allocates a copy

**Reproduced.** Both fallback paths in [`Key.cs`](src/Respsody/Key.cs#L85) assign an array range back to `pooledBuffer`. A range on `byte[]` creates a new array; it does not create a borrowed span. The original rental is lost, and the copy is returned to `ArrayPool`.

`Key.Utf8(new string('a', 257)).CalculateHashSlot()` throws `ArgumentException` when returning the non-bucket-sized copy. A 512-byte key avoids that exception but still allocates/copies and loses the original rental.

Keep the original array reference, operate on its written span, and return the original in cleanup. Test both direct hashing and serialization fallback, across the stack-buffer threshold and pool bucket boundaries.

### R7. Reset leaves parser state and drops buffer ownership

**Reproduced parser corruption; source-confirmed cleanup defects.**

- [`RespFraming`](src/Respsody/Resp/Parsing/RespFraming.cs) retains `CrLfParser` and `LengthParser` state across inherited reset. Feeding an interrupted `$12`, resetting, then feeding `$3\r\nabc\r\n` yields no frame. An interrupted `+old\r`, reset, then `+OK\r\n` throws from the stale `WaitingForLf` state.
- [`Framing.Reset`](src/Respsody/Network/Framing.cs#L242) clears its leased tail and replaces a written current block without returning those leases.
- [`RespFrameAggregationStrategy.Reset`](src/Respsody/Resp/RespFrameAggregationStrategy.cs#L112) discards active aggregate/streamed-string state without disposing it; stack entries can also retain references.

Reset the complete parser state and release each partial frame/aggregate ownership exactly once. Distinguish lost leases and retained references from a guaranteed permanent heap leak: finalization can recover some abandoned memory, but is not a substitute for prompt pool reuse. Test reset at every relevant framing boundary and during nested/streamed aggregation.

### R8. Late scalar replies are not released after timeout/cancellation

**Source-confirmed.** When completion has already been claimed, [`Payload.Complete(Frame<RespContext>, ...)`](src/Respsody/Client/RespClient.cs#L546) returns without disposing the frame or attribute. The aggregate overload has corresponding cleanup. Clearing `ReadyFrames` does not dispose the individual frames.

Keep the response queue entry until its reply is consumed so protocol ordering survives cancellation; then release the unclaimed reply's ownership. Test timeout/cancellation followed by a late reply and a successful next command, with lease accounting.

### R9. Combo has duplicate execution, ownership, and API defects

**Source-confirmed.**

- [`Builder4.Cmd(arg, factory)`](src/Respsody/Combo.cs#L150) calls `CreateCommand` twice. The fifth operation is packed twice, so a non-idempotent command can execute twice and the first future is unobserved. Both commands enter the Combo's ownership list; this is more serious than an unused local allocation.
- Fluent `Execute` methods call `parent.Execute()` but never dispose the parent Combo. Disposing the returned response tuple releases responses, not the commands stored in `_ownedCommands`. Repair command cleanup on success and failure while retaining delivery ownership until sends finish.
- The factory-only fourth/fifth-step `Cmd` overloads include an unused generic `TArg` ([lines 126 and 158](src/Respsody/Combo.cs#L126)), which ordinary `.Cmd(c => ...)` calls cannot infer.

Add small tests for exact execution count, four/five-command fluent compilation, and command/response cleanup when a member fails. Preserve one channel item per batch; a batch does not promise one socket write, one packet, or Redis transaction semantics.

### R10. Cluster refresh replaces clients without disposing them

**Source-confirmed.** [`UpdateClusterStateFromSeed`](src/Respsody/Cluster/ClusterRouter.cs#L128) creates a fresh seed client, selects it for the `myself` node, and disposes old clients only when their node IDs disappear. Refreshing the same seed can replace its client while retaining the old connection. Failed refreshes also lack cleanup for the fresh seed client.

Track client ownership by instance as well as node identity, and dispose abandoned clients on all outcomes. Separately, an in-flight refresh can publish/start clients after router disposal; coordinate refresh and shutdown without putting a blanket lock on every route lookup.

### R11. Redirect timeout subtracts only the milliseconds component

**Source-confirmed.** [`ExecuteWithAutoRedirections`](src/Respsody/Cluster/ClusterRouter.cs#L366) subtracts `elapsed.Milliseconds` rather than the complete elapsed duration. A 2.1-second first attempt consumes only 100 ms of its timeout budget. Use a complete elapsed-time/deadline calculation and test redirection after more than one second, including exhausted budgets.

### R12. Cluster selection has long-running and incomplete-topology edge cases

**Source-confirmed.** [`ClusterNode.PickAny/PickReplica`](src/Respsody/Cluster/ClusterNode.cs#L31) use signed increment/remainder indexing; counter wraparound can produce negative indexes for relevant node counts. Use overflow-safe indexing without extra synchronization.

[`SlotsMap.GetNode`](src/Respsody/Cluster/SlotsMap.cs#L49) can return an unassigned null entry despite its non-null return type. Make incomplete slot coverage an explicit routing outcome. Cover topology changes and missing slots; ordinary successful cluster traffic is insufficient.

### R13. Unsent disposal and lazy connection create unmatched delivery leases

**Source-confirmed.** [`Command.Dispose`](src/Respsody/Command.cs#L188) finalizes an unsent command before releasing its owner. Finalization creates a delivery lease even though no send will release it. Likewise, [`ExecuteCommand`](src/Respsody/Client/RespClient.cs#L122) finalizes before entering the lazy connection path; [`ConnectThenExecuteCommand`](src/Respsody/Client/RespClient.cs#L203) calls it again after connecting, adding a second delivery lease for one actual send.

Separate committing serialized data from acquiring ownership for an actual delivery, and release abandoned submissions on every failure path. Test create/dispose without execution, the first command on a lazily started client, and connection failure/cancellation. Keep repeated-command redirection ownership correct when changing finalization.

## Historical generator diagnosis

These fixes affect generated-code correctness or build determinism and need not add runtime overhead.

| Finding | Evidence and appropriate action |
| --- | --- |
| Equality compares an explication with itself (old C6) | [`Explication.Equals`](src/Respsody.Generators/Syntax/CommandNodeExplication.cs#L118) zips `_nodes` with itself. A HashSet can suppress distinct variations when node counts, hashes, and method-name preferences match. Summed hashes make permutations collide; it does **not** drop every equal-length variation. Compare ordered nodes and use an order-sensitive hash; compile representative overload sets. |
| Generated string literals are not escaped (old C7) | [`Build`](src/Respsody.Generators/Library/Build.cs#L86) and [`CommandSyntaxNode`](src/Respsody.Generators/Syntax/CommandSyntaxNode.cs#L77) interpolate values into C# source. Quotes/backslashes can produce invalid code or changed wire bytes. Use Roslyn literal construction/escaping and test the emitted bytes. This is a build-input/code-generation defect, not an established remote injection vulnerability. |
| Attribute recognition is imprecise (old H10) | [Semantic matching](src/Respsody.Generators/CommandsSourceGenerator.cs#L198) compares the immediate namespace name, admitting unrelated `Foo.Respsody.RespCommandAttribute`; the [syntax prefilter](src/Respsody.Generators/CommandsSourceGenerator.cs#L270) misses attribute aliases. Use exact symbol/metadata identity with a compatible discovery path. |
| Public identifiers depend on build culture (old M13) | Active `ToLower()` calls in [`Build`](src/Respsody.Generators/Library/Build.cs#L104), [the generator](src/Respsody.Generators/CommandsSourceGenerator.cs#L329), and [`OptionSyntaxNode`](src/Respsody.Generators/Syntax/OptionSyntaxNode.cs#L58) are culture-sensitive. Turkish lowercasing turns `INCR` into `ıncr`. Use invariant identifier transformations and compare generated APIs across cultures. |

Lower-priority API/diagnostic work: report useful errors for empty command definitions and unterminated backtick literals, and remove or explain the unused cancellation token on command-builder methods. Execution extensions do pass cancellation to `ExecuteCommand`; the unused builder parameter does not establish that command execution ignores cancellation.

## Corrections to the earlier review

The following table records the baseline assessment of earlier claims. The narrower confirmed issues (standalone blocks, RPC cleanup, and R10) are now fixed as recorded above. Withdrawn or conditional claims should not drive unnecessary changes to hot paths.

| Earlier claim | Validated disposition |
| --- | --- |
| C3: ordinary `NoOpPool` blocks lack an owner | Withdrawn for normal use: [`MemoryBlocks.Lease`](src/Respsody/Memory/MemoryBlocks.cs#L29) always attaches the owner. **Narrower real issue:** standalone public `MemoryBlock(int)`/`MemoryBlock(byte[])` construction leaves the finalizer without an owner. Define/fix that construction contract; do not blame the regular NoOpPool path. |
| H1: completion disposal requires atomics everywhere | `TryDispose` can race if the same response is concurrently disposed; no supported internal concurrent-disposal path was established. Completion already uses CAS. Define the ownership guarantee before adding an atomic to every response disposal. |
| H2: undisposed `SemaphoreSlim` is a high-severity leak | Cleanup concern in `DirectSocketRpc`; no `AvailableWaitHandle` use or native-handle leak was established. |
| H4: refresh mutates the collection being enumerated by disposal | Refresh constructs new node arrays/state. The concrete client-lifetime issues are R10. Missing `volatile` alone does not prove the originally claimed failure or justify a routing lock. |
| H5: subscription `Bytes` keys point into recycled responses | Withdrawn for inspected subscriptions: [command parsing](src/Respsody/Library/CommandParser.cs#L50) and [push parsing](src/Respsody/Resp/PushExtensions.cs#L18) call `ToArray()`. Public `Bytes` still borrows mutable memory; that is a separate caller-lifetime contract. |
| H9: tokenizer emits empty strings that crash `token[0]` | No reachable path found; tokenizer does not emit empty strings. |
| H12: hash expression precedence is a correctness bug | No equality/hash contract violation demonstrated by that mixing expression. |
| H13/H14: shared generator builders cause races/order failures | Existing calls are sequential; parameter-list mutation is an intentional ordering dependency, and some builders are freshly created. Maintainability observation, not an established failure. |
| M4: re-reading `Command._owners` instead of a local causes a bug | No independent defect established under the single-owner contract. Using the local would not make ownership thread-safe. |
| M5: `Framing.MakeMemory` inevitably leaks on exceptions | Exception-safety concern without a demonstrated trigger; prioritize concrete reset/late-response ownership failures. |
| M11/M15: summed or constant hashes are inherently incorrect | Collisions are legal. Summation aggravates C6; constant hashes are valid for node types whose instances all compare equal. |
| M21: GC fluctuations decrease cumulative allocation totals and cause endless retries | Unsupported. The harness reads cumulative allocated bytes, not current live heap size. A retry-loop defect needs an actual reproduction. |
| Missing `GC.SuppressFinalize` on `ClusterRouter` | Not a defect: this class has no finalizer. |
| Missing comments/renamed tests/unused helpers are high priorities | Style and cleanup do not outrank data integrity, pool reuse, or measurement quality. |

Other scoped observations recorded at the baseline (the concrete corrections and focused lifecycle tests are now implemented):

- `StringExtensions.EqualTo` uses culture-sensitive comparison for protocol tokens; ordinal semantics are more appropriate.
- Numeric readers accept some malformed input, and disposal guard checks differ between response types. Separate required protocol validation from detection of caller use-after-dispose; choose boundary checks with measured cost.
- `RespMap.ToMapOf` sizes a dictionary from aggregate element count, including header and both keys/values. This overallocates during materialization, not borrowed map access.
- `ProtocolWriter.ConvertToBulkString` creates an intermediate string and final byte array, but inspected generated call sites are **static preencoding**, not repeated command serialization.
- `ThreadStaticPool<T>` creates per-type clips and objects at initialization. This is a cold-start/retained-memory tradeoff to measure, not automatically a bug.
- Subscription cancellation/lifecycle, disposal during startup, and malformed topology input deserve focused contract tests before broad synchronization or validation changes.

## Historical benchmark diagnosis and measurement priorities

The README tables are historical results labelled .NET 9.0.5. Current utility projects target .NET 10, and the manual driver selects two sequential and ten concurrent iterations, while the tables show fifty. Historical dependency versions are not fully recorded alongside those tables. These results do not establish a universal fastest-client ranking for the current checkout.

### Baseline harness findings (corrections recorded above)

- [The BenchmarkDotNet entry point](util/Respsody.Benchmarks/Program.cs) selects only `SequentialBenchmark`. It starts embedded Garnet on port 6399. Parallel and buffer/framing benchmarks are not run by the default command. The manual harness expects a server on port 6379.
- [Manual CPU measurement](util/Respsody.Benchmarks.NonScientific/Library/CpuTime.cs#L7) busy-waits until process CPU time changes. Those probes add work to the measurement and also occur inside the allocation snapshot interval. Use measurement that does not manufacture CPU work.
- [Concurrent scenario counters](util/Respsody.Benchmarks.NonScientific/Tests.cs#L145) are updated by multiple continuations without synchronization. Return per-operation results and aggregate outside timed hot work where possible; do not add contended atomics merely to repair benchmark bookkeeping.
- Manual measurements are process-wide, use fixed client order, share a process with retained pools/caches, and include forced-GC behavior. Record those conditions and separate harness allocations from client costs.
- Both clients lack complete cleanup in the benchmark classes. Ensure comparable setup/teardown and validate results outside timing. Different returned counters in MSET/MGET cases expose weak validation, but do not mean Redis calls were skipped.
- `BuffersBenchmark` ignores the `CompareExchange` result and therefore does not implement mutual exclusion under contention. Its 256 KiB stack allocation is stack-size-sensitive. This dormant benchmark does not prove a race or overflow occurred in the published client comparisons.
- `SelectTaskRun` is misleadingly named but does not need `Task.Run`; adding scheduling would add overhead. Payload size 32786 is used consistently and is not a correctness defect merely because it differs from 32768.

### Workload and metric definitions

1. Establish deterministic correctness first: complete sends, exact command counts, identical results, and returned leases. A faster run that drops work or loses ownership is invalid.
2. Record commit, hardware, OS, SDK/runtime, GC settings, client/server versions, connection count, payload distribution, key encoding, concurrency, pipeline depth, and topology.
3. Measure CPU, allocated bytes/op, GC collections, retained memory, copies, throughput, and latency distributions. Separate cold initialization from warmed steady state and idle retention from loaded memory use.
4. Use small and large payloads, sequential and pipelined traffic, fragmentation, sustained saturation, cluster routing, and reconnect/cancellation workloads. Distinguish client-process CPU from server CPU, and record whether the server runs in the same process, a separate local process, or a remote host. BenchmarkDotNet's host starts Garnet while ordinary measurements run in a separate worker process; these processes still compete for machine resources.
5. Compare equivalent response consumption. Borrowed spans versus materialized arrays demonstrate a useful API advantage; also measure equal owned-output workloads where the application must retain results.
6. Define the operation. Parallel BenchmarkDotNet means normalized by `OperationsPerInvoke=12_500` are amortized throughput costs, **not individual request latency**. Sequential SET+GET and MSET+MGET entries report a pair per invocation; their op/s is not automatically Redis commands/s.
7. Compare before/after changes in Release. Optimize measured dominant costs, keeping checks at the appropriate ownership/protocol boundary. Avoid trading unbounded pool retention or extra connections for deceptively low allocation counts.

## Historical test coverage priorities

At `433d741`, the framing suites exercised many input/block-boundary combinations, ServerTests covered large payloads and basic Combo execution, and the container suites exercised concurrent traffic, repeated disposal, subscriptions/reconnection, and ordinary cluster routing. That coverage did not establish comprehensive numeric, partial-send, reset, or redirection correctness. Focused tests have since been added for these boundaries.

The follow-up added focused runtime regressions, generator compilation/behavior tests, CRC16/hash-tag vectors, and bounded lifecycle tests for disposal during active work. Even at the baseline, integration coverage indirectly exercised generated commands, serialization, and response types; describing those areas as wholly untested was inaccurate.

Use observable completion with bounded waits for callback-driven tests; use bounded readiness probes for external servers where no event is available. Replacing every `Task.Delay` with a `TaskCompletionSource` is not a general solution. Test naming and exception-style cleanup are secondary.

`ServerTests` uses embedded Garnet and does not need Docker. Baseline container fixtures used fixed names/ports and collided when fixture instances ran concurrently. The corrected fixtures use generated names and mapped host ports. See [AGENTS.md](AGENTS.md) for current build requirements and filters.
