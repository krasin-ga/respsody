using Respsody.Resp;

namespace Respsody.Exceptions;

public class RespUnexpectedResponseException(ResponseType expected, RespResponse response)
    : RespExceptionBase($"Expected response to be {expected}, but got `{response.ToDebugString()}`");