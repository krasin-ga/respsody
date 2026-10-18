# Architecture and performance research plan

Status: planned; experiments and implementation have not started under this plan. The existing comparison harness and preliminary source and prior-art observations are inputs to the research.

## Objective and scope

Determine which architectural changes would make Respsody safer to use and more predictable under load while advancing its goal of minimum RESP/Redis client overhead. Produce evidence and design decisions before changing the implementation.

This plan covers four research priorities:

1. Make ownership misuse harder and reduce lifecycle complexity.
2. Define overload behavior and investigate optional admission control.
3. Establish reproducible correctness and performance comparisons.
4. Measure and investigate the remaining costs per command.

Novelty is a secondary question: identify useful distinctions from existing clients without assuming familiar pooling, pipelining, or code-generation techniques are unique.

This document defines the research scope and acceptance criteria. Experiments, extensions to the existing benchmark harness, analyzer prototypes, API changes, and production implementation remain future work. Completing the research should produce a separate implementation proposal before applying a preferred design.

## Starting evidence

Read the [architecture and ownership guide](../architecture.md), [benchmark methodology](../benchmark-comparison.md), and [project instructions](../../AGENTS.md) before beginning.

The current design provides useful research starting points:

- Generated commands preencode static protocol fragments; commands and completion sources are pooled.
- Response structs borrow pooled storage. Copies share ownership state, aggregate children borrow the root, and published ValueTasks must be consumed once.
- Outgoing submission uses an unbounded channel. A Combo occupies one channel entry but may contain many commands and substantial serialized data.
- A payload allocates a completion guard. Small sends use a staging buffer; fragmented incoming frames can require a contiguous copy.
- The default send staging buffer is 512 KiB; incoming and outgoing memory-block settings are each 128 KiB. These are configuration sizes, not a formula for total memory per connection.
- Expiration is polled. Canceling an already-sent command does not remove its protocol position or undo server work.

Re-establish the test and performance baseline at the revision used for research. Measure correctness changes, including cancellation and channel-wait behavior, for allocation and CPU regressions.

## Sequence and decision checkpoints

| Stage | Work | Required output before proceeding |
| --- | --- | --- |
| 0 | Freeze a baseline and define supported contracts | Revision/configuration manifest, passing relevant checks, ownership and overload questions |
| 1 | Establish measurement methodology (track C) | Reproducible baseline, noise estimates, agreed operation definitions |
| 2 | Map ownership and overload behavior (tracks A and B) | State/ownership tables, overload traces, candidate designs and tradeoffs |
| 3 | Attribute costs and evaluate narrow experiments (track D) | Cost ledger and isolated before/after results |
| 4 | Compare candidates across all four tracks | Adopt, reject, defer, or insufficient-evidence decisions |
| 5 | Write an implementation proposal | Ordered, independently reviewable changes with compatibility and validation plans |

Source inspection for A and B can proceed while the measurement protocol is refined. Performance conclusions depend on stage 1. Serialize performance runs on a measurement host; do not overlap them with builds, other benchmarks, or the integration stress suite.

At stage 0, record the exact commit and working-tree state, then run the solution build and relevant tests, including Docker integration when validating the complete baseline. Classify failures before proceeding; do not quietly reduce workloads or exclude failed cases from the report.

## Track A: ownership safety and lifecycle complexity

Research question: can ownership rules become easier to enforce without adding ordinary-command allocations, synchronization, or copies?

Hypotheses to test:

- Some misuse can be diagnosed at compile time without affecting runtime performance.
- Fewer ownership transitions may remove failure paths and checks, even when the underlying pooling strategy stays intact.
- Any reuse of completion/disposal state needs a lifetime model that covers both late protocol activity and response aliases held by callers.

Inspect [Command](../../src/Respsody/Command.cs), [OutgoingBuffer](../../src/Respsody/Memory/OutgoingBuffer.cs), [completion sources](../../src/Respsody/Library/PooledValueTaskCompletionSource.cs), [CompletionGuard](../../src/Respsody/Client/CompletionGuard.cs), [DisposalGuard](../../src/Respsody/Client/DisposalGuard.cs), [RespClient](../../src/Respsody/Client/RespClient.cs), [Combo](../../src/Respsody/Combo.cs), response wrappers, and generated execution/build methods.

Research tasks:

1. Draw separate state models for command construction/delivery, future completion/consumption, and response ownership. Record which component owns each resource and exactly when ownership transfers or ends.
2. Enumerate success and failure transitions: unsent disposal, failed preparation/submission, lazy startup, cancellation before/after send, late replies, callback exceptions, partial batch failures, redirects, reconnect, and shutdown.
3. Map each transition to existing regression coverage. Distinguish supported usage from unsupported concurrent disposal, borrowed views escaping their owner, and other caller misuse.
4. Audit public escape hatches, including externally owned responses, manual commands, copied response structs, and copied Combo/builders. Account for command construction sharing thread-local state.
5. Inventory applicable compiler/analyzer checks before proposing project-specific diagnostics. Evaluate diagnostics for lost root responses, unconsumed or multiply consumed ValueTasks, incorrect ownership transfer, and obvious borrowed-view escape. Document cases static analysis cannot reliably prove.
6. Compare retaining the API with diagnostics, simplifying internal ownership states, and introducing explicit owner/view APIs. Assess generated code, async usage, source/binary compatibility, and migration effort separately.

Future experiment design: compile representative correct and incorrect examples, measure diagnostic accuracy and build-time cost, and use instrumented ownership counters plus deterministic fault injection to check proposed lifecycle changes. Instrumentation should remain outside the normal measured production path.

Required evidence: every resource has a defined terminal state; response copies and late callbacks cannot access recycled ownership state; exceptional paths finish futures and release leases exactly once. Returning a completion source after GetResult is not evidence that a response or disposal guard can also be recycled then.

Decision output: a supported-usage contract, transition/coverage matrix, analyzer feasibility report, and ranked API/internal-design options. Prefer a smaller ownership model over additional checks when both provide equivalent safety and measured performance. Explicitly record limitations and false-positive risks.

## Track B: overload behavior and admission control

Research question: how should the client behave when producers exceed transport/server capacity or a connection remains unavailable?

Hypotheses to test:

- An unbounded queue can minimize admission overhead at sustainable load while allowing memory and queue delay to grow during overload.
- Optional admission limits may improve useful throughput, memory stability, and tail latency under overload, but their accounting and waiting mechanisms have costs.
- Bounding channel entries alone cannot bound serialized bytes, in-flight replies, waiting producers, or caller-retained response storage.

Inspect [StructuredSocket](../../src/Respsody/Network/StructuredSocket.cs), its pending queue and send loop, [RespClient](../../src/Respsody/Client/RespClient.cs), [Combo](../../src/Respsody/Combo.cs), [ClusterRouter](../../src/Respsody/Cluster/ClusterRouter.cs), and both option types.

Research tasks:

1. Define candidate policies: existing unbounded behavior, immediate rejection at a limit, and asynchronous waiting for admission. Decide what callers observe and when command timeouts begin for each candidate.
2. Compare count, byte, and combined budgets. Separate unsent work, sent-but-unanswered commands, response buffers, and caller-owned memory. Identify allocations made before admission, including manually built commands.
3. Specify budget ownership and release events. A canceled in-flight command may still occupy a protocol slot until its reply or disconnection; do not release a resource budget merely because the caller stopped waiting.
4. Define Combo admission in terms of members/bytes rather than one queue item. Investigate whole-batch admission, oversized batches, fairness between large and small producers, and rejection cleanup.
5. Define per-connection, per-node, and optional client-wide limits for clusters. Account for redirects, connection initialization, subscription recovery, and potential deadlocks when control commands compete with ordinary traffic.
6. Characterize disconnected queues, slow/non-reading peers, delayed replies, cancellation storms, and recovery after overload. Include idle operation and ordinary load with admission control disabled.

Future experiment design: use controlled server delay/connection failure and an offered-load sweep below, near, and above measured capacity. Use fixed producer concurrency as well as independently scheduled arrivals. Record admission waits and rejections instead of silently slowing producers or dropping requests.

Required metrics: offered/admitted/sent/completed/rejected/expired counts, queue entries and bytes, in-flight count, waiting producers, retained/leased memory, CPU, allocations, admission delay, end-to-end percentiles, and recovery time. Unknown response sizes and caller-retained roots must be reported as limits on any memory guarantee.

Decision output: an overload contract and policy comparison. Recommend whether admission control is justified, what it actually bounds, and which behavior should remain the default. Measure the cost of the disabled path; do not assume an optional feature is free.

## Track C: reproducible measurements and comparisons

Research question: which workloads demonstrate the benefits and costs of this architecture, and can another developer reproduce the conclusions?

Start with the [BenchmarkDotNet entry point](../../util/Respsody.Benchmarks/Program.cs), [manual harness](../../util/Respsody.Benchmarks.NonScientific/Program.cs), existing benchmark classes, and the [comparison methodology and limitations](../benchmark-comparison.md).

Inspect settings before running. The BenchmarkDotNet entry point selects `SequentialBenchmark` in code. The manual harness defaults to SET+GET comparisons configured through command-line options, with separate worker processes and borrowed/owned results reported separately. Its `--legacy` mode retains scenarios selected in code and a thread-pool cap; the default comparison does not impose that cap or force GC.

### Measurement contract

- Pin source revisions, dependencies, SDK/runtime, client/server versions, server configuration, and container image digest where applicable. Record CPU, OS, logical processors, GC mode, tiering/PGO settings, thread-pool configuration, affinity if changed, topology, connection count, warmup, and timing boundaries.
- The library targets .NET 8 and current utilities target .NET 10. Define the consumer runtime explicitly; compare clients under the same runtime in each run. Treat .NET 8 and .NET 10 results as separate experiments.
- Separate protocol/serialization microbenchmarks from whole-client measurements. Run the server outside the client process for client CPU measurements; record server CPU independently. Distinguish Docker Desktop networking, native local networking, and a separate host.
- Define the unit for every result: Redis command, SET+GET pair, batch, logical cluster operation, or materialized response. Report batch latency as batch latency. Track useful completed work and failures, not only attempted operations.
- Compare equivalent semantics and output ownership. Test borrowed-buffer consumption and owned-output workloads separately. Include pooled/lease APIs offered by comparison clients where applicable; never compare one client's borrowed result to another client's mandatory string conversion without labeling the difference.
- Use a correctness mode that verifies exact command counts, response bytes, order, and ownership. In timed runs, consume all futures/responses, check failures, and describe which validation remains inside timing. Final database contents alone do not prove all intermediate replies were correct.
- Use both closed-loop concurrency and open-loop offered load. Measure open-loop latency from intended arrival so producer/admission stalls are visible; report missed schedules, rejected work, and generator saturation. Measure service latency separately from total latency.
- Separate clean timing runs from diagnostic profiling. Quantify instrumentation overhead, and avoid shared hot counters or CPU probes that manufacture work.
- Include fresh-process cold-start measurements, warmed steady state, and post-load retained memory. Do not force GC inside a timed steady-state interval; document any pre-run collection policy and compare against normal-GC operation where it matters.

### Workload matrix

Use a small core matrix first, then expand one dimension at a time. Avoid a full Cartesian product before identifying the relevant costs.

| Priority | Cases | Purpose |
| --- | --- | --- |
| Core | GET/SET, integer replies, MGET/MSET; 0 B, 32 B, 256 B, 4 KiB, and 32 KiB values | Fixed versus payload-dependent costs; define hits/misses and response consumption |
| Core | Sequential, depths of 8/32/128, multiple producer counts, generated commands and Combo | Coordination, batching, throughput, and latency tradeoffs |
| Core | Both borrowed reads and equal owned byte/string output | Separate ownership/API advantage from equivalent materialization cost |
| Memory | 1 MiB and selected larger values; ASCII/Unicode/binary keys; long keys across stack/pool boundaries | Encoding, copying, large allocations, and retained memory |
| Protocol | Nested maps/arrays, streamed values, controlled fragmentation around block boundaries, and varying segment counts | Parser/aggregation cost and lifetime behavior |
| Load | Sustainable load, saturation, overload, mixed small/large commands, burst then idle | Tail latency, fairness, memory growth, and recovery |
| Lifecycle | Cancellation before/after send, late replies, stalled peers, reconnect, slow consumers, and partial batch failure | Correctness under failure and failure-path overhead |
| Cluster/subscription | Primary/replica routing, grouping, MOVED/ASK, topology refresh, subscription churn and reconnect | Costs hidden by single-node GET/SET results |

Compare against pinned stable .NET clients using their relevant efficient APIs, starting with StackExchange.Redis. Consult primary documentation/source for each selected version; distinguish released APIs from main-branch features. Cross-language clients can inform design/prior-art research, but differing runtimes require a separate, explicitly scoped performance comparison.

### Repetition and interpretation

Run an initial variance study before choosing measurement duration or accepting small differences. Alternate or randomize candidate/baseline order, use independent fresh-process repetitions, and retain every run with its status. Aim for at least seven paired runs for shortlisted candidates; increase the run length/count when uncertainty remains. Predeclare invalid-run conditions and retain their evidence rather than discarding inconvenient results.

Report raw runs, medians, variability, and confidence intervals based on independent runs rather than treating correlated requests as independent trials. Record completed sample counts for p95/p99/p99.9; do not publish a high percentile without enough tail observations. Define the smallest worthwhile improvement and tolerated regressions before measuring a candidate.

Required artifacts: runnable source/configuration, exact commands, revision/environment manifest, correctness results, raw measurements, analysis scripts, operation definitions, and a reproduction guide. Bring any useful external probes into reproducible research artifacts during the future research phase; current machine-local paths are not sufficient publication evidence.

Decision output: a trusted baseline and bounded claims such as "lower client CPU for this payload/depth/runtime," with known bottlenecks and uncertainty. A universal fastest-client claim requires a defined comparison scope and evidence beyond this initial matrix.

## Track D: remaining command costs

Research question: which costs dominate validated workloads, and which changes improve them without transferring cost into memory retention, rare failures, or caller complexity?

Build a cost ledger before proposing optimizations. Attribute wall time, client CPU, allocation, copies, synchronization, socket operations, and retained memory to the stages in the architecture guide. Separate startup, response materialization, compile-time work, and the warmed command path.

| Candidate | Investigation | Constraints and counterexamples |
| --- | --- | --- |
| Completion guard and pooled futures | Measure allocations by type and lifetime; compare unchanged guards, fewer independent state objects, or carefully versioned ownership designs | Late replies, response aliases, abandoned futures, and generation reuse/wraparound must remain safe; do not pool a guard merely because its future was consumed |
| Send staging and scatter/gather | Sweep payload/segment counts, staging sizes, and copy-versus-send thresholds; correlate bytes copied with send calls and CPU | More syscalls can outweigh saved copies; preserve exact bytes, ordering, partial-write progress, and cancellation |
| Fragmented-frame copies | Measure frequency and retained-memory cost; compare current contiguous frames with alternative parsing/view strategies | Span-based public APIs may require contiguity; segmented storage can move complexity or copying to callers |
| Expiration scanning | Measure CPU/allocation against outstanding commands, canceled-but-unanswered entries, and scan intervals; investigate deadline-oriented alternatives | Account for maintenance per command, cancellation latency, tick wraparound, shutdown, and any per-command registrations or allocations |
| Channels, response queues, continuations | Profile synchronization, scheduling, and idle-to-busy transitions under different producer counts | Include idle wakeup and disconnect tests; inline continuations can move application work onto receive processing |
| Pool sizing and retention | Measure active versus idle leases, size classes, thread-local clips, boundary exchanges, and connection churn; follow the magazine-pool investigation below | Reduced allocations must not hide excessive retained memory or cross-thread contention; pool budgets are not total-memory caps |
| Generated serialization and scalar parsing | Measure generated/manual paths, encoded arguments, numeric boundaries, code size, JIT cost, and generator/build overhead | Do not weaken protocol validation or assume fewer source operations means faster machine code |

Select experiments from measured dominance, not perceived novelty. Change one mechanism at a time in an isolated research branch when research execution is authorized. Keep the baseline implementation as a control and preserve identical workloads, validated results, and ownership semantics.

Every promising microbenchmark result must survive a whole-client comparison and applicable lifecycle tests. If an effect is below the noise floor, extend measurement or defer the change. If a correctness requirement adds cost, document the cost and evaluate alternatives rather than removing the requirement.

Decision output: a ranked ledger of fixed and payload-dependent costs, measured improvements/regressions, rejected experiments, and the smallest justified implementation candidates.

### Thread-local clips: prior art and boundary behavior

Research question: does the current single-clip pool provide the best CPU/retention tradeoff for Respsody's actual allocation and return patterns, and which aspects are established techniques versus project-specific choices?

Starting evidence: [ThreadStaticPool<T>](../../src/Respsody/Library/ThreadStaticPool.cs) uses one thread-local clip of eight objects per closed generic type, with shared full/empty clip queues. Its static initializer creates one full and one empty clip per logical processor. Local hits use stack push/pop; exchanging whole clips amortizes access to shared queues. This source inspection does not establish a throughput benefit or a worst-case exchange-rate bound.

The closest identified prior art is Bonwick and Adams's 2001 magazine layer: local stacks of object pointers exchange full and empty magazines with a shared depot. It also retains a previous local magazine to prevent boundary thrashing. [Original paper, sections 3.1 and 3.3](https://www.usenix.org/legacy/event/usenix01/full_papers/bonwick/bonwick_html/)

| Respsody | Magazine-layer counterpart |
| --- | --- |
| Eight-object clip | Bounded stack of object pointers |
| Thread-local active clip | CPU-local loaded magazine |
| FullClips and EmptyClips queues | Shared depot of full and empty magazines |
| Exchange at underflow/overflow | Batch replenishment and return |

TCMalloc provides related prior art for local caches, batch transfers, and cross-thread reuse. Its transfer-cache design is a comparison point, not an assertion that its implementation is identical. [TCMalloc design](https://github.com/google/tcmalloc/blob/master/docs/design.md)

Describe the current technique as a thread-local object pool with magazine-style batch exchange. Generic managed objects, thread-local rather than CPU-local state, concurrent queues, fixed capacity, and integration with client lifetimes are implementation choices. The identified prior art rules out treating the core batch-exchange idea as new; any narrower novelty claim needs separate evidence. Pin the TCMalloc revision when conducting the formal comparison.

Boundary hypothesis from source inspection: start with one object in the active clip and repeat Get, Get, Return, Return, returning only the two objects just obtained. With capacity eight, the local occupancy follows this cycle:

| Call | Local occupancy afterward | Shared interaction |
| --- | --- | --- |
| First Get | 0 | None |
| Second Get | 7 | Publish empty clip; obtain full clip |
| First Return | 8 | None |
| Second Return | 1 | Publish full clip; obtain empty clip |

This predicts two clip exchanges per four pool calls. Each exchange enqueues a clip and attempts a dequeue; missing shared clips cause allocation. The trace is an inference from the implementation, not an executed benchmark or evidence of a whole-client regression. The paper's second local magazine addresses the corresponding oscillation. [Original paper, section 3.1](https://www.usenix.org/legacy/event/usenix01/full_papers/bonwick/bonwick_html/)

Future research tasks:

1. Confirm the occupancy trace with isolated state and valid object ownership. Sweep initial occupancy and burst sizes around clip capacity; include balanced Get/Return pairs, long runs in one direction, and mixed patterns. Keep setup and cleanup outside timing.
2. Map real pool consumers and their rent/return threads. Include same-thread reuse, producer/consumer handoff, async continuation migration, multiple connections, and thread churn. Measure whether boundary oscillation occurs often enough to affect client CPU.
3. Compare the unchanged single-clip implementation with a two-local-clip candidate and a small capacity sweep. Compare both equal individual-clip size and equal total local capacity, so the effect of an extra clip is distinguishable from simply retaining more objects. All prototypes remain future work.
4. Record CPU/time per pool call, shared queue attempts and misses, allocations by type, GC activity, retained object/clip counts, and cross-thread throughput. Measure fresh-process initialization separately: each newly used closed generic type eagerly constructs eight objects per logical processor, plus clip/stack storage. Include burst-then-idle retention and cold misses.
5. Use separate diagnostic and timing runs under track C's protocol. Verify no simultaneously leased duplicate objects, lost returns, or reset-contract changes. Do not add shared instrumentation counters to the measured hot path.
6. Validate promising candidates in whole-client workloads with equivalent response consumption, producer counts, and payloads. Report throughput, CPU, allocation, latency percentiles, retained memory, and implementation complexity together.

Decision output: a cited prior-art comparison, measured exchange patterns, and an adopt/reject/defer decision for additional local clips or capacity changes. Preserve the current implementation unless evidence justifies a change. A lower microbenchmark exchange count alone is insufficient; account for extra retained objects, initialization cost, and effects on cross-thread reuse.

## Research deliverables and exit criteria

Future deliverables should include:

- An ownership contract and state/coverage matrix, including unsupported usage and diagnostic feasibility.
- An overload/admission policy comparison with resource-accounting and compatibility implications.
- A reproducible baseline package and benchmark report with raw data and uncertainty.
- A cost ledger and candidate decision table covering CPU, allocations, copies, throughput, tail latency, retained memory, complexity, and compatibility.
- A short prior-art comparison of command generation, borrowed aggregate responses, magazine-style pooling, and overload policies, using pinned primary sources. Include single- versus two-clip results and distinguish established techniques from project-specific combinations; do not claim first use without evidence.
- A separate implementation proposal ordering accepted changes by evidence and dependencies. Keep each issue independently reviewable, with validation and rollback criteria.

Research is complete when each of the four priorities has an evidence-backed adopt/reject/defer decision, unresolved questions are explicit, and another developer can reproduce the measurements. "No change justified" is a valid result. Do not use increased architectural complexity, a lower allocation count alone, or a better microbenchmark alone as proof of an improvement.

## Completion checklist

- [ ] Baseline revision, environment, supported contracts, and test results recorded.
- [ ] Measurement protocol validated and noise quantified.
- [ ] Ownership transitions and misuse-detection options evaluated.
- [ ] Overload policies and their actual resource bounds compared.
- [ ] Dominant command costs attributed and shortlisted experiments measured.
- [ ] Clip-pool prior art, boundary oscillation, and single-/two-clip CPU and retention tradeoffs evaluated.
- [ ] Correctness, lifecycle behavior, tail latency, and retained memory checked for each candidate.
- [ ] Raw evidence and reproduction instructions retained in portable artifacts.
- [ ] Decisions and a separate implementation proposal written; production changes remain a later task.
