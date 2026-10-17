using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespBigNumber : IRespResponse
{
    private readonly Frame<RespContext> _frame;
    private readonly DisposalGuard _guard;

    public RespBigNumber(Frame<RespContext> frame, DisposalGuard guard)
    {
        Debug.Assert(CanConvert(frame));

        _frame = frame;
        _guard = guard;
    }

    public BigInteger ToBigInteger()
    {
        var span = _frame.Span[1..^2];

        var sign = 1;
        if (!span.IsEmpty && span[0] is (byte)'-' or (byte)'+')
        {
            sign = span[0] == '-' ? -1 : 1;
            span = span[1..];
        }

        if (span.IsEmpty)
            throw new FormatException("Invalid RESP big number.");

        BigInteger number = 0;
        foreach (var value in span)
        {
            var digit = value - '0';
            if ((uint)digit > 9)
                throw new FormatException("Invalid RESP big number.");
            number = number * 10 + digit;
        }

        return number * sign;
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
        var cloned = new RespBigNumber(frame, _guard.ToCheckOnly());
        return (Unsafe.As<RespBigNumber, T>(ref cloned), lifetime);
    }

    public static bool CanConvert(in Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.BigNumber;
    }
}
