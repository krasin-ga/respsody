namespace Respsody.Generators.Library;

internal static class LinqExtensions
{
    public static int UncheckedSum(this IEnumerable<int> enumerable)
    {
        return enumerable.Aggregate(0, (acc, e) => unchecked(acc + e));
    }

    public static int UncheckedSum<T>(this IEnumerable<T> enumerable, Func<T, int> selector)
    {
        return enumerable.Aggregate(0, (acc, e) => unchecked(acc + selector(e)));
    }
}