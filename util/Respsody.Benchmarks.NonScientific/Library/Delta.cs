namespace Respsody.Benchmarks.NonScientific.Library;

public readonly record struct Delta<T>(T Before, T After)
    where T : IComparable<T>
{
    public Delta<T> EnsureIncreasingOrEq()
    {
        if (After.CompareTo(Before) <= 0)
            throw new InvalidOperationException($"After:{After} <= Before:{Before}");

        return this;
    }

    public bool IsIncreasingOrEq => After.CompareTo(Before) >= 0;
}