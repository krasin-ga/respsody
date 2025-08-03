namespace Respsody.Library.Disposables;

public readonly struct DisposableTuple<T1, T2>(
    (T1, IDisposable) v1,
    (T2, IDisposable) v2)
    : IDisposable
{
    public void Dispose()
    {
        v1.Item2.Dispose();
        v2.Item2.Dispose();
    }

    public T1 Value1 { get; init; } = v1.Item1;
    public T2 Value2 { get; init; } = v2.Item1;

    public void Deconstruct(
        out T1 value1,
        out T2 value2)
    {
        value1 = Value1;
        value2 = Value2;
    }
}


public readonly struct DisposableTuple<T1, T2, T3>(
    (T1, IDisposable) v1,
    (T2, IDisposable) v2,
    (T3, IDisposable) v3)
    : IDisposable
{
    public void Dispose()
    {
        v1.Item2.Dispose();
        v2.Item2.Dispose();
        v3.Item2.Dispose();
    }

    public T1 Value1 { get; init; } = v1.Item1;
    public T2 Value2 { get; init; } = v2.Item1;
    public T3 Value3 { get; init; } = v3.Item1;

    public void Deconstruct(
        out T1 value1,
        out T2 value2,
        out T3 value3)
    {
        value1 = Value1;
        value2 = Value2;
        value3 = Value3;
    }
}

public readonly struct DisposableTuple<T1, T2, T3, T4>(
    (T1, IDisposable) v1,
    (T2, IDisposable) v2,
    (T3, IDisposable) v3,
    (T4, IDisposable) v4)
    : IDisposable
{
    public void Dispose()
    {
        v1.Item2.Dispose();
        v2.Item2.Dispose();
        v3.Item2.Dispose();
        v4.Item2.Dispose();
    }

    public T1 Value1 { get; init; } = v1.Item1;
    public T2 Value2 { get; init; } = v2.Item1;
    public T3 Value3 { get; init; } = v3.Item1;
    public T4 Value4 { get; init; } = v4.Item1;

    public void Deconstruct(
        out T1 value1,
        out T2 value2,
        out T3 value3,
        out T4 value4)
    {
        value1 = Value1;
        value2 = Value2;
        value3 = Value3;
        value4 = Value4;
    }
}

public readonly struct DisposableTuple<T1, T2, T3, T4, T5>(
    (T1, IDisposable) v1,
    (T2, IDisposable) v2,
    (T3, IDisposable) v3,
    (T4, IDisposable) v4,
    (T5, IDisposable) v5)
    : IDisposable
{
    public void Dispose()
    {
        v1.Item2.Dispose();
        v2.Item2.Dispose();
        v3.Item2.Dispose();
        v4.Item2.Dispose();
        v5.Item2.Dispose();
    }

    public T1 Value1 { get; init; } = v1.Item1;
    public T2 Value2 { get; init; } = v2.Item1;
    public T3 Value3 { get; init; } = v3.Item1;
    public T4 Value4 { get; init; } = v4.Item1;
    public T5 Value5 { get; init; } = v5.Item1;

    public void Deconstruct(
        out T1 value1,
        out T2 value2,
        out T3 value3,
        out T4 value4,
        out T5 value5)
    {
        value1 = Value1;
        value2 = Value2;
        value3 = Value3;
        value4 = Value4;
        value5 = Value5;
    }
}
