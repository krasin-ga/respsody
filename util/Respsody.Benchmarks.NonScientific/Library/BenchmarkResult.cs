using System.Globalization;
using System.Reflection;
using System.Text;

namespace Respsody.Benchmarks.NonScientific.Library;

public record BenchmarkResult(
    string Scenario,
    string Target,
    Dataset Dataset,
    int Iterations,
    TimeSpan TotalElapsed,
    TimeSpan BestRun,
    TimeSpan CpuTime,
    [property: Skip] TimeSpan MinWastedCpuTime,
    SizeInBytes TotalAllocated,
    [property: Skip] SizeInBytes TotalMemory)
{
    private static readonly PropertyInfo[] Properties = typeof(BenchmarkResult).GetProperties()
        .Where(p => p.GetCustomAttribute<SkipAttribute>() is null).ToArray();

    public static string WriteTable(IEnumerable<BenchmarkResult> results)
    {
        var resultList = results.ToList();
        var columnWidths = CalculateColumnWidths(resultList);

        var sb = new StringBuilder();
        //sb.AppendLine(BuildSeparator(columnWidths));

        sb.AppendLine(BuildRow(Properties.Select(p => p.Name), columnWidths));
        sb.AppendLine(BuildSeparator(columnWidths));

        for (var i = 0; i < resultList.Count; i++)
        {
            var result = resultList[i];
            var values = Properties.Select(p => FormatValue(p.GetValue(result)!));
            sb.AppendLine(BuildRow(values, columnWidths));
            if ((i + 1) % 2 == 0 && i != resultList.Count - 1)
                sb.AppendLine(BuildRow(Properties.Select(_ => FormatValue("-")), columnWidths));
        }

        if (resultList.Count % 2 != 0)
            sb.AppendLine(BuildSeparator(columnWidths));
        return sb.ToString();
    }

    private static Dictionary<string, int> CalculateColumnWidths(IEnumerable<BenchmarkResult> results)
    {
        var widths = Properties.ToDictionary(p => p.Name, p => p.Name.Length);

        foreach (var result in results)
        foreach (var prop in Properties)
        {
            var value = prop.GetValue(result)!;
            var formatted = FormatValue(value);
            widths[prop.Name] = Math.Max(widths[prop.Name], formatted.Length);
        }

        return widths;
    }

    private static string BuildSeparator(Dictionary<string, int> columnWidths)
        => "|" + string.Join("|", columnWidths.Values.Select(w => new string('-', w + 2))) + "|";

    private static string BuildRow(IEnumerable<string> values, Dictionary<string, int> columnWidths)
    {
        var props = Properties.ToArray();
        var sb = new StringBuilder();
        var i = 0;
        foreach (var val in values)
        {
            var prop = props[i++];
            var alignRight = IsNumericType(prop.PropertyType);
            var width = columnWidths[prop.Name];
            if (alignRight)
                sb.Append($"| {val.PadLeft(width)} ");
            else
                sb.Append($"| {val.PadRight(width)} ");
        }

        sb.Append('|');
        return sb.ToString();
    }

    private static bool IsNumericType(Type type)
        => type == typeof(TimeSpan) || type == typeof(SizeInBytes) || type == typeof(int);

    private static string FormatValue(object? value)
    {
        if (value is TimeSpan ts)
            return FormatShort(ts);
        return value?.ToString() ?? "";
    }

    private static string FormatShort(TimeSpan ts)
    {
        if (ts.TotalSeconds >= 1)
            return $"{ts.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)}s";
        if (ts.TotalMilliseconds >= 1)
            return $"{ts.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}ms";
        if (ts.TotalMilliseconds >= 0.001)
            return $"{(ts.TotalMilliseconds * 1000).ToString("F2", CultureInfo.InvariantCulture)}µs";
        return $"{ts.Ticks} ticks";
    }
}