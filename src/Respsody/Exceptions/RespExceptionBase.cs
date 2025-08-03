namespace Respsody.Exceptions;

public class RespExceptionBase: Exception
{
    public RespExceptionBase()
    {
    }

    public RespExceptionBase(string? message): base(message)
    {
    }

    public RespExceptionBase(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}