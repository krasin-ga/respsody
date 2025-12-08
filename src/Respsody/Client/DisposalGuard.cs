namespace Respsody.Client;

public readonly struct DisposalGuard(CompletionGuard completionGuard, bool checkOnly)
{
    public bool TryDispose() => !checkOnly && completionGuard.TryDispose();
    public void CheckDisposed() => completionGuard.CheckDisposed();

    public static DisposalGuard CheckOnly(CompletionGuard completionGuard)
        => new(completionGuard, checkOnly: true);

    public static implicit operator DisposalGuard(CompletionGuard guard)
    {
        return new DisposalGuard(guard, checkOnly: false);
    }

    public DisposalGuard ToCheckOnly() => new(completionGuard, checkOnly: true);
}