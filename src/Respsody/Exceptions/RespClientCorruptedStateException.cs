namespace Respsody.Exceptions;

public class RespClientCorruptedStateException(string error)
    : RespExceptionBase($"PANIC! State has been corrupted; the client will stop its operations. Reason: {error}");