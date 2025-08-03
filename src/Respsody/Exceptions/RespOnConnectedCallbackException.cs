namespace Respsody.Exceptions;

public class RespOnConnectedCallbackException(Exception innerException) 
    : RespExceptionBase("Handler.OnConnected() failed to execute. See inner exception for details. Will try to reconnect.", innerException);