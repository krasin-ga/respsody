using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Buffers.Text;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespNumber : IRespResponse
{
    private readonly Frame<RespContext> _frame;
    private readonly DisposalGuard _guard;

    public RespNumber(Frame<RespContext> frame, DisposalGuard guard)
    {
        Debug.Assert(CanConvert(frame));

        _frame = frame;
        _guard = guard;
    }

    public long ToInt64()
    {
        _guard.CheckDisposed();

        var span = _frame.Span[1..^2];

        if (Utf8Parser.TryParse(span, out long number, out var consumed) && consumed == span.Length)
            return number;

        throw new FormatException("Invalid RESP integer.");
    }

    public void Dispose()
    {
        if(_guard.TryDispose())
            _frame.Dispose();
    }

    public (T, IDisposable) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse
    {
        var (frame, lifetime) = _frame.AsExternallyOwned();
        var cloned = new RespNumber(frame, _guard.ToCheckOnly());
        return (Unsafe.As<RespNumber, T>(ref cloned), lifetime);
    }
    public static bool CanConvert(in Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Number;
    }
}
