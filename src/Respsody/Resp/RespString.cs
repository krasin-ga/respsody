using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespString : IRespResponse
{
    private const int FormatLength = 3;
    private const int VerbatimOffset = FormatLength + 1;

    private readonly Frame<RespContext> _frame;
    private readonly CompletionGuard _guard;

    public RespString(Frame<RespContext> frame, CompletionGuard guard)
    {
        Debug.Assert(CanConvert(frame));

        _frame = frame;
        _guard = guard;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanConvert(Frame<RespContext> frame)
    {
        return frame.Context.Type is RespType.SimpleString or RespType.BulkString
            or RespType.BulkError or RespType.SimpleError or RespType.Null or RespType.VerbatimString;
    }

    public override string? ToString()
    {
        return ToString(DefaultRespEncoding.Value);
    }

    public string? ToString(Encoding encoding)
    {
        return IsNull()
            ? null
            : encoding.GetString(GetSpan());
    }

    public bool IsNull()
    {
        return _frame.GetRespType() is RespType.Null
               || _frame.Context.Length == -1;
    }

    public ReadOnlySpan<char> AsUnicodeSpan()
    {
        return IsNull() ? [] : MemoryMarshal.Cast<byte, char>(GetSpan());
    }

    public bool TryGetVerbatimFormat(out ReadOnlySpan<byte> span)
    {
        if (_frame.GetRespType() != RespType.VerbatimString)
        {
            span = default;
            return false;
        }

        span = _frame.Memory.Span.Slice(_frame.Context.DataOffset, FormatLength);
        return true;
    }

    public ReadOnlySpan<byte> GetSpan()
    {
        switch (_frame.GetRespType())
        {
            case RespType.SimpleString:
            case RespType.BulkString:
            case RespType.SimpleError:
            case RespType.BulkError:
                return _frame.Memory[_frame.Context.DataOffset..^2].Span;
            case RespType.VerbatimString:
                //skip format and ':'
                return _frame.Memory[(_frame.Context.DataOffset + VerbatimOffset)..^2].Span;
            case RespType.Null:
                return Span<byte>.Empty;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Dispose()
    {
        if(_guard?.CanDispose() is not false) 
            _frame.Dispose();
    }

    public (T, IDisposable) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse
    {
        var (frame, lifetime) = _frame.AsExternallyOwned();
        var cloned = new RespString(frame, CompletionGuard.Restrictive);
        return (Unsafe.As<RespString, T>(ref cloned), lifetime);
    }
}