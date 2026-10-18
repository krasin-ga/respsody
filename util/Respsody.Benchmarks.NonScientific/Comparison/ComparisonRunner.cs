using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace Respsody.Benchmarks.NonScientific.Comparison;

internal static class ComparisonRunner
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] Clients = ["Respsody", "StackExchange.Redis"];
    private static readonly string[] EnvironmentVariables =
    [
        "DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_gcServer", "DOTNET_GCHeapCount",
        "DOTNET_GCHeapHardLimit", "DOTNET_PROCESSOR_COUNT", "COMPlus_TieredCompilation", "COMPlus_TieredPGO"
    ];

    public static async Task Run(string[] args)
    {
        if (args is ["--comparison-worker", var path])
        {
            await ComparisonWorker.Run(path);
            return;
        }
        if (args is ["--help"])
        {
            Console.WriteLine("""
                Isolated SET+GET comparison (Release build, external Redis server).
                --endpoint 127.0.0.1:6379
                --payloads 256                 Comma-separated byte counts, including 0
                --concurrency 1,32             Independent sequential SET+GET lanes
                --responses both               borrowed, owned, or both
                --rounds 7                     Paired fresh-process repetitions
                --pairs-per-lane 256           Starting calibration workload
                --warmup-seconds 1
                --minimum-seconds 3            Calibration target for each client
                --minimum-cpu-seconds 0.5       Calibration target for each client
                --worker-timeout-seconds 600
                --output artifacts/comparisons
                --legacy                       Run the previous exploratory scenarios
                """);
            return;
        }
        EnsureRelease();
        var options = ComparisonOptions.Parse(args);
        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        var directory = Path.GetFullPath(Path.Combine(options.Output, runId));
        Directory.CreateDirectory(directory);
        Console.WriteLine($"Results: {directory}");
        Console.WriteLine("Work: SET then GET; 2 Redis commands/pair; RESP3; database 0; preencoded keys.");
        Console.WriteLine("CPU/throughput, allocations, and pair latency use separate fresh processes. No forced GC or thread-pool cap.");
        if (options.Rounds < 7 || options.MinimumSeconds < 3 || options.MinimumCpuSeconds < 0.5)
            Console.WriteLine("Short run settings: use these for validation; they provide limited performance evidence.");

        using var cancellation = new ConsoleCancellation();
        var token = cancellation.Token;
        var results = new List<WorkerResult>();
        var sequence = 0;
        string? serverId = null;
        WorkerResult? baseline = null;
        try
        {
            var serverConfiguration = await ReadServerConfiguration(options.Endpoint);
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
            {
                Options = options,
                CommandLine = args,
                AssemblySha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Assembly.GetExecutingAssembly().Location, token))),
                ServerConfiguration = serverConfiguration,
                Environment = EnvironmentVariables.ToDictionary(name => name, Environment.GetEnvironmentVariable),
                Operation = "SET+GET pair; one outstanding command per lane; identical binary payload and preencoded keys",
                Consumption = "Length and first/last byte checksum inside timing; all bytes checked before and after; all futures awaited and leases disposed",
                Latency = "Closed-loop SET+GET pair latency, including response consumption, measured in separate workers",
                Limitations = "Requires a dedicated stable Redis server/host; native client scheduling/buffering and background work remain part of each client cost"
            }, Json), token);

            foreach (var payload in options.Payloads)
            foreach (var concurrency in options.Concurrency)
            foreach (var response in options.Responses)
            {
                var pairs = options.InitialPairsPerLane;
                Console.WriteLine($"Calibrating {payload} B, {concurrency} lanes, {response}...");
                for (var attempt = 0; ; attempt++)
                {
                    if (attempt >= 12 || (long)pairs * concurrency > 10_000_000)
                        throw new InvalidOperationException("Calibration exceeded its workload limit. Inspect raw samples and adjust settings explicitly.");
                    double scale = 1;
                    foreach (var client in OrderedClients(attempt))
                    {
                        var sample = await Execute(client, "calibration", -1, pairs);
                        scale = Math.Max(scale, options.MinimumSeconds / Math.Max(sample.ElapsedSeconds, 0.000001));
                        if (options.MinimumCpuSeconds > 0)
                            scale = Math.Max(scale, sample.CpuSeconds > 0 ? options.MinimumCpuSeconds / sample.CpuSeconds.Value : 4);
                    }
                    if (scale <= 1)
                        break;
                    pairs = checked((int)Math.Ceiling(pairs * Math.Min(16, Math.Max(2, scale * 1.25))));
                }
                Console.WriteLine($"Using {pairs:N0} pairs/lane for both clients ({2L * pairs * concurrency:N0} commands/run).");

                for (var round = 0; round < options.Rounds; round++)
                    foreach (var phase in new[] { "cpu", "allocation", "latency" })
                    foreach (var client in OrderedClients(round))
                    {
                        Console.WriteLine($"  Round {round + 1}/{options.Rounds}: {client}, {phase}");
                        var sample = await Execute(client, phase, round, pairs);
                        results.Add(sample);
                        if (phase == "cpu" && (sample.ElapsedSeconds < options.MinimumSeconds || sample.CpuSeconds < options.MinimumCpuSeconds))
                            Console.WriteLine("  Sample below the requested measurement window; retained in results. Interpret small differences cautiously.");
                        await File.WriteAllTextAsync(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(results, Json), token);
                    }

                async Task<WorkerResult> Execute(string client, string phase, int round, int pairsPerLane)
                {
                    var request = new WorkerRequest(options.Endpoint, client, response, payload, concurrency, pairsPerLane,
                        options.WarmupSeconds, phase, round, $"respsody-comparison:{runId}");
                    var requestPath = Path.Combine(directory, $"{sequence++:D4}-{client}-{phase}.json");
                    await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, Json), token);
                    var result = await StartWorker(requestPath, options.WorkerTimeoutSeconds, token);
                    var currentServerId = result.ServerInfo.Split('\n').FirstOrDefault(line => line.StartsWith("run_id:", StringComparison.Ordinal))?.Trim();
                    if (currentServerId is null || (serverId is not null && currentServerId != serverId))
                        throw new InvalidOperationException("Redis server identity changed or could not be verified. Results are not comparable.");
                    serverId = currentServerId;
                    if (baseline is not null && (result.Runtime != baseline.Runtime || result.OS != baseline.OS ||
                        result.Architecture != baseline.Architecture || result.Processors != baseline.Processors ||
                        result.ServerGC != baseline.ServerGC || result.GCLatencyMode != baseline.GCLatencyMode ||
                        result.MinWorkerThreads != baseline.MinWorkerThreads || result.MaxWorkerThreads != baseline.MaxWorkerThreads ||
                        result.RespsodyVersion != baseline.RespsodyVersion || result.StackExchangeVersion != baseline.StackExchangeVersion))
                        throw new InvalidOperationException("Worker runtime or client configuration changed during the comparison.");
                    baseline ??= result;
                    if (result.CompletedCommands != 2L * pairsPerLane * concurrency)
                        throw new InvalidOperationException("Worker completed an unexpected command count.");
                    return result;
                }
            }

            var finalConfiguration = await ReadServerConfiguration(options.Endpoint);
            await File.WriteAllTextAsync(Path.Combine(directory, "server-config-after.json"), JsonSerializer.Serialize(finalConfiguration, Json), token);
            if (!serverConfiguration.Keys.SequenceEqual(finalConfiguration.Keys, StringComparer.Ordinal) ||
                !serverConfiguration.Values.SequenceEqual(finalConfiguration.Values, StringComparer.Ordinal))
                throw new InvalidOperationException("Redis configuration changed during the comparison. Inspect the saved configurations.");
            var summary = Summarize(results, options);
            await File.WriteAllTextAsync(Path.Combine(directory, "summary.md"), summary, token);
            Console.WriteLine(summary);
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), error.ToString(), CancellationToken.None);
            throw;
        }
    }

    private static IEnumerable<string> OrderedClients(int round) => round % 2 == 0 ? Clients : Clients.Reverse();

    private static void EnsureRelease()
    {
#if DEBUG
        throw new InvalidOperationException("Run the comparison with -c Release.");
#endif
    }

    private static async Task<WorkerResult> StartWorker(string requestPath, int timeoutSeconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate current executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--comparison-worker");
        start.ArgumentList.Add(requestPath);
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
        var stdout = worker.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = worker.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await worker.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            if (!worker.HasExited)
                worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            await File.WriteAllTextAsync(requestPath + ".stdout.txt", await stdout, CancellationToken.None);
            await File.WriteAllTextAsync(requestPath + ".stderr.txt", await stderr, CancellationToken.None);
        }
        if (worker.ExitCode != 0)
            throw new InvalidOperationException($"Worker exited with {worker.ExitCode}; see {requestPath}.stderr.txt");
        return JsonSerializer.Deserialize<WorkerResult>(await File.ReadAllTextAsync(requestPath + ".result.json", token))!;
    }

    private static async Task<SortedDictionary<string, string>> ReadServerConfiguration(string endpoint)
    {
        var options = ConfigurationOptions.Parse(endpoint);
        options.Protocol = RedisProtocol.Resp3;
        options.AllowAdmin = true;
        using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var server = connection.GetServer(connection.GetEndPoints().Single());
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "appendonly", "save", "maxmemory", "maxmemory-policy", "io-threads", "io-threads-do-reads", "hz", "activedefrag" })
        foreach (var entry in await server.ConfigGetAsync(name))
            values.Add(entry.Key, entry.Value);
        return values;
    }

    private static string Summarize(List<WorkerResult> results, ComparisonOptions options)
    {
        var text = new StringBuilder("Medians across independent workers; ranges are observed min–max. Latencies are median per-run SET+GET pair percentiles from separate closed-loop runs.\n\n");
        var shortSamples = results.Count(sample => sample.Request.Phase == "cpu" &&
            (sample.ElapsedSeconds < options.MinimumSeconds || sample.CpuSeconds < options.MinimumCpuSeconds));
        text.AppendLine(CultureInfo.InvariantCulture, $"CPU samples below requested windows: {shortSamples}. Requested windows: {options.MinimumSeconds} s elapsed, {options.MinimumCpuSeconds} s CPU.\n");
        text.AppendLine("| Bytes | Lanes | Response | Client | Commands/s (range) | CPU µs/command (range) | Alloc B/command | Pair samples/run | Pair p50 µs | Pair p95 µs | Pair p99 µs |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var group in results.GroupBy(sample => (sample.Request.PayloadBytes, sample.Request.Concurrency, sample.Request.Response, sample.Request.Client)))
        {
            var cpu = group.Where(sample => sample.Request.Phase == "cpu").ToArray();
            var allocations = group.Where(sample => sample.Request.Phase == "allocation");
            var latency = group.Where(sample => sample.Request.Phase == "latency").ToArray();
            text.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key.PayloadBytes} | {group.Key.Concurrency} | {group.Key.Response} | {group.Key.Client} | {Range(cpu.Select(sample => sample.CompletedCommands / sample.ElapsedSeconds))} | {Range(cpu.Select(sample => sample.CpuSeconds!.Value * 1_000_000 / sample.CompletedCommands))} | {Median(allocations.Select(sample => (double)sample.AllocatedBytes!.Value / sample.CompletedCommands)):F2} | {latency.Min(sample => sample.LatencySamples)} | {OptionalMedian(latency.Select(sample => sample.PairP50Microseconds))} | {OptionalMedian(latency.Select(sample => sample.PairP95Microseconds))} | {OptionalMedian(latency.Select(sample => sample.PairP99Microseconds))} |");
        }
        text.AppendLine("\nCPU includes runtime, GC, client background work, and the shared driver. Allocation/latency instrumentation is absent from CPU runs. Raw samples include collection counts and environment metadata. Tail percentiles require at least 100 observations beyond the percentile per run; '-' indicates insufficient samples. These results apply only to this workload and environment.");
        return text.ToString();
    }

    private static string Range(IEnumerable<double> values)
    {
        var samples = values.ToArray();
        return FormattableString.Invariant($"{Median(samples):F2} ({samples.Min():F2}–{samples.Max():F2})");
    }

    private static double Median(IEnumerable<double> values)
    {
        var samples = values.Order().ToArray();
        return (samples[(samples.Length - 1) / 2] + samples[samples.Length / 2]) / 2;
    }

    private static string OptionalMedian(IEnumerable<double?> values)
    {
        var samples = values.ToArray();
        return samples.Any(value => value is null) ? "-"
            : Median(samples.Select(value => value!.Value)).ToString("F2", CultureInfo.InvariantCulture);
    }

    private sealed class ConsoleCancellation : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private bool _disposed;

        public ConsoleCancellation()
        {
            Console.CancelKeyPress += Cancel;
        }

        public CancellationToken Token => _source.Token;

        private void Cancel(object? sender, ConsoleCancelEventArgs args)
        {
            lock (_source)
            {
                if (_disposed)
                    return;
                args.Cancel = true;
                _source.Cancel();
            }
        }

        public void Dispose()
        {
            Console.CancelKeyPress -= Cancel;
            lock (_source)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _source.Dispose();
            }
        }
    }
}
