using Respsody.Client;
using Respsody.Memory;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Respsody.Resp;

public readonly struct RespBoolean : IRespResponse
{
    private readonly Frame<RespContext> _frame;
    private readonly DisposalGuard _guard;

    public RespBoolean(Frame<RespContext> frame, DisposalGuard guard)
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
        var cloned = new RespBoolean(frame, _guard.ToCheckOnly());
        return (Unsafe.As<RespBoolean, T>(ref cloned), lifetime);
    }

    public bool ToBool()
    {
        _guard.CheckDisposed();

        var span = _frame.Span;
        if (span.Length == 4)
        {
            if (span[1] == 't')
                return true;
            if (span[1] == 'f')
                return false;
        }

        throw new FormatException("Invalid RESP boolean.");
    }

    public void Dispose()
    {
        if(_guard.TryDispose()) 
            _frame.Dispose();
    }
}
