namespace Respsody.Benchmarks.NonScientific.Library;

public static class EnumerableExtensions
{
    public static IEnumerable<Task<TResult>> SelectTaskRun<T, TResult>(this IEnumerable<T> enumerable, Func<T, Task<TResult>> func)
    {
        return enumerable.Select(func);
    }

    public static IEnumerable<Task> SelectTaskRun<T>(this IEnumerable<T> enumerable, Func<T, Task> func)
    {
        return enumerable.Select(func);
    }
}
