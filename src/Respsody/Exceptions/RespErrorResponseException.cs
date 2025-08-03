namespace Respsody.Exceptions;

public class RespErrorResponseException(string error) : RespExceptionBase(error)
{
    public string Error { get; } = error;
}