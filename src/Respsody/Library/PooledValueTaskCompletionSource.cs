using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace Respsody.Library;

public sealed class PooledValueTaskCompletionSource<TResult> : IValueTaskSource<TResult>, IValueTaskSource, ITaskCompletionSource
{
    private ManualResetValueTaskSourceCore<TResult> _core;

    public static PooledValueTaskCompletionSource<TResult> Create(bool runContinuationsAsynchronously)
    {
        var value = ThreadStaticPool<PooledValueTaskCompletionSource<TResult>>.Get();
        value._core.RunContinuationsAsynchronously = runContinuationsAsynchronously;
        return value;
    }

    public ValueTask<TResult> AsValueTask() => new(this, _core.Version);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);

    public void Reset()
    {
        _core.Reset();
        ThreadStaticPool<PooledValueTaskCompletionSource<TResult>>.Return(this);
    }

    public void SetException(Exception exception)
    {
        _core.SetException(exception);
    }

    public void SetResult<TResultG>(TResultG result)
    {
        _core.SetResult(Unsafe.As<TResultG, TResult>(ref result));
    }

    public TResult GetResult(short token)
    {
        var isValid = token == _core.Version;
        try
        {
            return _core.GetResult(token);
        }
        finally
        {
            if (isValid)
                Reset();
        }
    }

    void IValueTaskSource.GetResult(short token)
    {
        var isValid = token == _core.Version;
        try
        {
            _ = _core.GetResult(token);
        }
        finally
        {
            if (isValid)
                Reset();
        }
    }
}