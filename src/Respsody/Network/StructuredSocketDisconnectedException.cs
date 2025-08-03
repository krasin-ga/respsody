namespace Respsody.Network;

public class StructuredSocketDisconnectedException(string? message, Exception? innerException)
    : Exception(message, innerException);