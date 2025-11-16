using Respsody.Client;
using Respsody.Memory;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Respsody.Resp;

public readonly struct RespBoolean : IRespResponse
{
    private readonly Frame<RespContext> _frame;
    private readonly CompletionGuard _guard;

    public RespBoolean(Frame<RespContext> frame, CompletionGuard guard)
    {
        Debug.Assert(frame.GetRespType() is RespType.Boolean);

        _frame = frame;
        _guard = guard;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.Context.Type is RespType.Boolean;
    }

    public (T, IDisposable) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse
    {
        var (frame, lifetime) = _frame.AsExternallyOwned();
        var cloned = new RespBoolean(frame, CompletionGuard.Restrictive);
        return (Unsafe.As<RespBoolean, T>(ref cloned), lifetime);
    }

    public bool ToBool()
    {
        return _frame.Span[1] == 't';
    }

    public void Dispose()
    {
        if(_guard.CanDispose()) 
            _frame.Dispose();
    }
}