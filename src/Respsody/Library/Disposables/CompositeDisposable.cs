namespace Respsody.Library.Disposables;

internal sealed class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _disposables = new(16);

    public void Add(IDisposable disposable)
        => _disposables.Add(disposable);

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();

        _disposables.Clear();
    }
}