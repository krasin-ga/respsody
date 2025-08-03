using System.Diagnostics;
using System.Runtime.CompilerServices;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespBoolean : IDisposable, IRespResponse
{
    private readonly Frame<RespContext> _frame;

    public RespBoolean(Frame<RespContext> frame)
    {
        Debug.Assert(frame.GetRespType() is RespType.Boolean);

        _frame = frame;
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
        var cloned = new RespBoolean(frame);
        return (Unsafe.As<RespBoolean, T>(ref cloned), lifetime);
    }

    public bool ToBool()
    {
        return _frame.Span[1] == 't';
    }

    public void Dispose()
    {
        _frame.Dispose();
    }
}