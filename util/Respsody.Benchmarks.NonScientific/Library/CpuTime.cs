namespace Respsody.Benchmarks.NonScientific.Library;

public static class CpuTime
{
    public static TimeSpan GetActualValue()
    {
        var beforeTime = Environment.CpuUsage.TotalTime;
        while (beforeTime == Environment.CpuUsage.TotalTime)
            Thread.SpinWait(1);

        return Environment.CpuUsage.TotalTime;
    }
}