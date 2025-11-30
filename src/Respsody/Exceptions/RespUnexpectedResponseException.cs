using Respsody.Memory;
using Respsody.Resp;

namespace Respsody.Exceptions;

public class RespUnexpectedResponseException : RespExceptionBase
{
    public RespUnexpectedResponseException(ResponseType expected, RespResponse response)
        : base($"Expected response to be {expected}, but got `{response.ToDebugString()}`")
    {
    }

    public RespUnexpectedResponseException(ResponseType expected, Frame<RespContext> response)
        : base($"Expected response to be {expected}, but got `{response.ToDebugString()}`")
    {
    }

    public RespUnexpectedResponseException(ResponseType expected, RespAggregate agg)
        : base($"Expected response to be {expected}, but got `{agg.ToDebugString()}`")
    {
    }
}