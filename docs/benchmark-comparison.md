# Comparing client CPU and throughput

The manual benchmark's default entry point runs isolated Respsody and StackExchange.Redis workers. It currently compares SET followed by GET at configurable payload sizes and concurrency, with borrowed and owned response contracts reported separately. The older scenario collection remains available with `--legacy`; it uses process-wide measurements, fixed client order, shared process caches and pools, and forced GC.

## Running

Use a dedicated external Redis server with RESP3 support. The driver expects a plain host:port endpoint, database 0, and permission to read INFO and selected CONFIG GET settings. It does not change server configuration or flush a database. Pause other workloads on the client and server hosts when collecting performance evidence.

```powershell
dotnet run -c Release --project util/Respsody.Benchmarks.NonScientific -- --endpoint 127.0.0.1:6379
```

Defaults: 256-byte payloads, concurrency 1 and 32, both response contracts, seven paired repetitions, at least one second of warmup per worker. Calibration seeks at least three seconds elapsed and 0.5 seconds process CPU for each client. It selects one common command count for the pair; later samples below these targets are retained and flagged rather than silently retried or discarded.

```powershell
dotnet run -c Release --project util/Respsody.Benchmarks.NonScientific -- --endpoint 127.0.0.1:6379 --payloads 32,256,4096 --concurrency 1,8,32 --responses both --rounds 7
dotnet run -c Release --project util/Respsody.Benchmarks.NonScientific -- --help
```

The complete default comparison starts many fresh processes and can take several minutes. For a functional smoke run, explicitly reduce the workload; its timing is not strong performance evidence:

```powershell
dotnet run -c Release --project util/Respsody.Benchmarks.NonScientific -- --endpoint 127.0.0.1:6379 --payloads 0,256 --concurrency 1,8 --responses both --rounds 2 --pairs-per-lane 32 --warmup-seconds 0.02 --minimum-seconds 0 --minimum-cpu-seconds 0
```

## Matched conditions

- Each measured worker creates only its selected client. Workers run serially, in fresh processes using the same executable/runtime configuration. Client order alternates between paired rounds. Server CPU belongs to its separate process.
- Both clients use RESP3, database 0, one command connection, identical binary keys, identical payload bytes, and the same number of SET+GET pairs. Each lane has its own key and waits for SET and then GET; concurrency is the number of lanes, not a per-lane pipeline depth. Native scheduling, buffers, pool policies, and background tasks remain part of each client's implementation.
- Keys are preencoded for both clients. Nonempty values use byte memory. For zero-byte payloads, Respsody uses its supported empty-string Value representation because its empty byte-memory Value is currently treated as unset; the wire payload is empty for both clients.
- Borrowed mode uses Respsody's disposable response and StackExchange.Redis's StringGetLeaseAsync, consuming each response before disposal. Owned mode materializes Respsody's bytes into an array and uses StackExchange.Redis's byte-array result. Arrays are consumed immediately; this scenario does not model caller retention.
- All command futures are awaited. Timing includes response length checks, a first/last-byte checksum, and lease disposal. Separate pre/post checks compare complete payload bytes. This does not exhaustively validate ordering or all intermediate bytes under faults; lifecycle tests remain necessary.
- Warmup runs outside measurement. CPU and throughput use one continuous interval with no allocation counter reads or per-pair timestamps inside it. Allocation measurements and pair-latency measurements run in separate, independently warmed processes with the same fixed workload. No thread-pool cap or forced GC is applied.
- The process CPU delta includes the selected client's runtime, GC, networking, and background work, plus the common driver. Zero deltas are retained. Startup is excluded, so this is a warmed comparison, not a cold-start benchmark.

## Artifacts and interpretation

Each invocation creates a unique directory under `artifacts/comparisons/` (override with `--output`). It retains options, executable hash, selected runtime environment variables, server configuration, every worker request, calibration and measurement results, stdout/stderr, and any failure. Results record process IDs, runtime/OS/architecture, logical processor count, GC/thread-pool settings, client versions, Redis INFO, collection counts, and completed command counts. Server run identity is checked across workers; selected configuration values are compared before and after the run.

`results.json` holds measured samples; `summary.md` reports medians and observed min/max ranges. One SET+GET pair is **two Redis commands**. CPU microseconds per command and allocated bytes per command use this denominator. Latency is the duration of the **whole pair**, measured separately from throughput; it is closed-loop latency and excludes time waiting outside the driver. Tail percentiles are omitted when fewer than 100 observations lie beyond that percentile. Raw sample counts accompany the results.

Keep zero CPU samples and failed-run artifacts. Do not claim a precise CPU advantage from samples below counter resolution, overlapping ranges, or a short smoke run. The output does not compute confidence intervals or establish a universal ranking. The wider workload and statistical work remains in the [research plan](plans/architecture-performance-research.md).

For reproducible publication, also record the checkout commit and dirty diff, CPU model, power settings, server hardware/OS and container image digest, network topology, and other host activity. A stable server identity/configuration cannot detect competing traffic or temperature/frequency changes. Runtime settings inherited by workers must match between runs; inspect the saved manifest and runtime configuration files.

Workers delete only their generated run-prefixed keys during normal cleanup. A forcibly terminated worker or unavailable server may leave those keys behind. Worker failures stop the comparison; failed samples are not silently replaced.

## Implementation validation

On 2026-10-18, the Release solution build and 19 focused option/delta tests passed. An isolated Redis 7.4.7 smoke run exercised both clients, both response contracts, 0/256-byte values, concurrency 1/8, and two paired rounds: 96 measured workers plus calibration. Artifact checks confirmed equal command counts/checksums within pairs, separate process IDs, alternating client order, and separate CPU/allocation/latency instrumentation. A further 24-worker run exercised calibration with 0.5-second elapsed and 0.05-second CPU targets; all CPU samples reached those targets. A forced worker timeout stopped the comparison, preserved failure logs, and left no child process running. These checks validate the driver; their short runs do not establish a client performance ranking.
