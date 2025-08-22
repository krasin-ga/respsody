namespace Respsody.Exceptions;

public class RespClusterInitializationFailedException(IReadOnlyList<Exception> innerExceptions)
    : RespExceptionBase("Cluster initialization failed", innerExceptions[0])
{
    public IReadOnlyList<Exception> Exceptions { get; init; } = innerExceptions;
}