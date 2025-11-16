namespace Respsody.Client;

public sealed class CompletionGuard
{
    public static readonly CompletionGuard Restrictive = new() { _isCompleted = 1, _isDisposed = true };
    private int _isCompleted;
    private bool _isDisposed;

    public bool CanComplete()
    {
        return Interlocked.CompareExchange(ref _isCompleted, 1, 0) == 0;
    }

    public bool CanDispose()
    {
        if (_isDisposed)
            return false;
        
        _isDisposed = true;
        return true;
    }
}