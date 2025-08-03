namespace Respsody.Memory;

internal sealed class EmptyDisposable : IDisposable
{
    public static readonly EmptyDisposable Instance = new();

    private EmptyDisposable()
    {
    }

    public void Dispose()
    {
    }
}