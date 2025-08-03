namespace Respsody.Library.Disposables;

public readonly struct Defer(Action deferAction) : IDisposable
{
    public void Dispose()
    {
        deferAction();
    }
}