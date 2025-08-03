namespace Respsody.Exceptions;

public class RespUnexpectedOperationException(string error) : RespExceptionBase(error);