using System;
using System.Globalization;
using System.Linq;

namespace Respsody.Benchmarks.NonScientific.Comparison;

public sealed record ComparisonOptions
{
    public string Endpoint { get; init; } = "127.0.0.1:6379";
    public int[] Payloads { get; init; } = [256];
    public int[] Concurrency { get; init; } = [1, 32];
    public string[] Responses { get; init; } = ["borrowed", "owned"];
    public int Rounds { get; init; } = 7;
    public int InitialPairsPerLane { get; init; } = 256;
    public double WarmupSeconds { get; init; } = 1;
    public double MinimumSeconds { get; init; } = 3;
    public double MinimumCpuSeconds { get; init; } = 0.5;
    public int WorkerTimeoutSeconds { get; init; } = 600;
    public string Output { get; init; } = "artifacts/comparisons";

    public static ComparisonOptions Parse(string[] args)
    {
        var options = new ComparisonOptions();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length)
                throw new ArgumentException($"Missing value for {args[i]}.");
            var value = args[i + 1];
            options = args[i] switch
            {
                "--endpoint" => options with { Endpoint = value },
                "--payloads" => options with { Payloads = Integers(value, 0, 16 * 1024 * 1024) },
                "--concurrency" => options with { Concurrency = Integers(value, 1, 1024) },
                "--responses" when value == "both" => options with { Responses = ["borrowed", "owned"] },
                "--responses" when value is "borrowed" or "owned" => options with { Responses = [value] },
                "--rounds" => options with { Rounds = Integer(value, 1, 100) },
                "--pairs-per-lane" => options with { InitialPairsPerLane = Integer(value, 1, 10_000_000) },
                "--warmup-seconds" => options with { WarmupSeconds = Number(value, 0, 300) },
                "--minimum-seconds" => options with { MinimumSeconds = Number(value, 0, 300) },
                "--minimum-cpu-seconds" => options with { MinimumCpuSeconds = Number(value, 0, 300) },
                "--worker-timeout-seconds" => options with { WorkerTimeoutSeconds = Integer(value, 1, 3600) },
                "--output" => options with { Output = value },
                _ => throw new ArgumentException($"Unknown option or value: {args[i]} {value}.")
            };
        }
        if (string.IsNullOrWhiteSpace(options.Endpoint) || string.IsNullOrWhiteSpace(options.Output))
            throw new ArgumentException("Endpoint and output must not be empty.");
        return options;
    }

    private static int[] Integers(string value, int minimum, int maximum) =>
        value.Split(',').Select(item => Integer(item, minimum, maximum)).Distinct().ToArray();

    private static int Integer(string value, int minimum, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
            throw new ArgumentException($"Expected an integer between {minimum} and {maximum}: {value}.");
        return parsed;
    }

    private static double Number(string value, double minimum, double maximum)
    {
        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed) || parsed < minimum || parsed > maximum)
            throw new ArgumentException($"Expected a number between {minimum} and {maximum}: {value}.");
        return parsed;
    }
}
