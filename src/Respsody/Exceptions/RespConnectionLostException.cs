namespace Respsody.Exceptions;

public class RespConnectionLostException(string? message = null)
    : RespExceptionBase(message ?? "Connection to server was lost");