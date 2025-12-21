using System.Diagnostics;

namespace Respsody.Client;

public sealed class CompletionGuard
{
    private int _isCompleted;
    private bool _isDisposed;

    public bool TryComplete()
    {
        return Interlocked.CompareExchange(ref _isCompleted, 1, 0) == 0;
    }

    public bool TryDispose()
    {
        if (_isDisposed)
            return false;
        
        _isDisposed = true;
        return true;
    }

    [StackTraceHidden]
    public void CheckDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, typeof(CompletionGuard));
    }
}