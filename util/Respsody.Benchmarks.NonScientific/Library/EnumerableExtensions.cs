namespace Respsody.Benchmarks.NonScientific.Library;

public static class EnumerableExtensions
{
    public static IEnumerable<Task> SelectTaskRun<T>(this IEnumerable<T> enumerable, Func<T, Task> func)
    {
        return enumerable.Select(func);
    }
}