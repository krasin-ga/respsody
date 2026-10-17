using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespDouble : IRespResponse
{
    private readonly Frame<RespContext> _frame;
    private readonly DisposalGuard _guard;

    public RespDouble(Frame<RespContext> frame, DisposalGuard guard)
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

        return ParseLiteralOrThrow(span);
    }

    private static double ParseLiteralOrThrow(ReadOnlySpan<byte> span)
    {
        if (span.SequenceEqual("inf"u8) || span.SequenceEqual("+inf"u8))
            return double.PositiveInfinity;
        if (span.SequenceEqual("-inf"u8))
            return double.NegativeInfinity;
        if (span.SequenceEqual("nan"u8) || span.SequenceEqual("+nan"u8) || span.SequenceEqual("-nan"u8))
            return double.NaN;

        throw CreateFormatException(span);
    }

    private static FormatException CreateFormatException(ReadOnlySpan<byte> span)
    {
        return new FormatException($"Can't parse `{DefaultRespEncoding.Value.GetString(span)}`");
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
        var cloned = new RespDouble(frame, _guard.ToCheckOnly());
        return (Unsafe.As<RespDouble, T>(ref cloned), lifetime);
    }

    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Double;
    }
}
