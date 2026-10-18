namespace Respsody.Benchmarks.NonScientific.Library;

public static class CpuTime
{
    public static TimeSpan GetActualValue()
    {
        return Environment.CpuUsage.TotalTime;
    }
}
