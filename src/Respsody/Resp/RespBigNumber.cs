using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Respsody.Exceptions;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespBigNumber : IRespResponse
{
    private readonly Frame<RespContext> _frame;

    public RespBigNumber(Frame<RespContext> frame)
    {
        Debug.Assert(CanConvert(frame));

        _frame = frame;
    }

    public static async ValueTask<RespBigNumber> FromResponseTask(
        ValueTask<RespResponse> responseTask)
    {
        var response = await responseTask;
        try
        {
            if (response.Frame is not { } sliceMemory)
                throw new RespUnexpectedResponseException(ResponseType.BigNumber, response);

            return sliceMemory.ToRespBigNumber();
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public BigInteger ToBigInteger()
    {
        var span = _frame.Span[1..^2];

        var sign = 1;
        BigInteger number = 0;

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
        var cloned = new RespBigNumber(frame);
        return (Unsafe.As<RespBigNumber, T>(ref cloned), lifetime);
    }

    public static bool CanConvert(in Frame<RespContext> frame)
    {
        return frame.GetRespType() is RespType.BigNumber;
    }
}