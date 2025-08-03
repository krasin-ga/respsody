using System.Diagnostics;
using System.Runtime.CompilerServices;
using Respsody.Exceptions;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespNumber : IRespResponse
{
    private readonly Frame<RespContext> _frame;

    public RespNumber(Frame<RespContext> frame)
    {
        Debug.Assert(CanConvert(frame));

        _frame = frame;
    }

    public static async ValueTask<RespNumber> FromResponseTask(
        ValueTask<RespResponse> responseTask)
    {
        var response = await responseTask;
        try
        {
            if (response.Frame is not { } sliceMemory)
                throw new RespUnexpectedResponseException(ResponseType.Number, response);

            return sliceMemory.ToRespNumber();
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public long ToInt64()
    {
        var span = _frame.Span[1..^2];

        var sign = 1;
        var number = 0;

        for (var i = 0; i < span.Length; i++)
        {
            var @byte = span[i];

            if (i == 0 && @byte == '-')
            {
                sign = -1;
                continue;
            }

            number = number * 10 + (@byte - '0');
        }

        return number * sign;
    }

    public void Dispose()
    {
        _frame.Dispose();
    }

    public (T, IDisposable) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse
    {
        var (frame, lifetime) = _frame.AsExternallyOwned();
        var cloned = new RespNumber(frame);
        return (Unsafe.As<RespNumber, T>(ref cloned), lifetime);
    }
    public static bool CanConvert(in Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.Number;
    }
}