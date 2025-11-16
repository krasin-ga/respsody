using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespDouble : IRespResponse
{
    private readonly Frame<RespContext> _frame;
    private readonly CompletionGuard _guard;

    public RespDouble(Frame<RespContext> frame, CompletionGuard guard)
    {
        Debug.Assert(CanConvert(frame));

        _frame = frame;
        _guard = guard;
    }
    public double ToDouble()
    {
        var span = _frame.Span[1..^2];

        if (Utf8Parser.TryParse(span, out double parsed, out var bytesConsumed)
            && span.Length == bytesConsumed)
            return parsed;

        return ParseLiteralsNaiveOrThrow(span);
    }

    private static double ParseLiteralsNaiveOrThrow(Span<byte> span)
    {
        if (span.Length is not (3 or 4))
            throw CreateFormatException(span);

        const byte n = (byte)'n';
        const byte f = (byte)'f';
        const byte i = (byte)'i';
        const byte plus = (byte)'+';
        const byte minus = (byte)'-';

        //(+|-)?(inf|nan)
        switch (span[^1])
        {
            case n:
                return double.NaN;
            case f:
                switch (span[0])
                {
                    case i:
                    case plus:
                        return double.PositiveInfinity;
                    case minus:
                        return double.NegativeInfinity;
                }
                break;
        }

        throw CreateFormatException(span);
    }

    private static FormatException CreateFormatException(Span<byte> span)
    {
        return new FormatException($"Can't parse `{DefaultRespEncoding.Value.GetString(span)}`");
    }

    public void Dispose()
    {
        if(_guard.CanDispose())
            _frame.Dispose();
    }

    public (T, IDisposable) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse
    {
        var (frame, lifetime) = _frame.AsExternallyOwned();
        var cloned = new RespDouble(frame, CompletionGuard.Restrictive);
        return (Unsafe.As<RespDouble, T>(ref cloned), lifetime);
    }

    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Double;
    }
}