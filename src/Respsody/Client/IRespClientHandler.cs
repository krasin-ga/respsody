namespace Respsody.Client;

public interface IRespClientHandler
{
    /// <summary>
    /// Called when the client's state got corrupted.
    /// </summary>
    void OnPanic(IRespClient client, Exception exception);

    /// <summary>
    /// Called when the client successfully connects.
    /// </summary>
    ValueTask OnConnected(IRespClient client, int generation);

    /// <summary>
    /// Called when the client gets disconnected.
    /// </summary>
    void OnDisconnected(IRespClient client, Exception? exception, int generation);

    /// <summary>
    /// Called when an exception occurs during connection.
    /// </summary>
    void OnConnectionError(IRespClient client, Exception exception);

    /// <summary>
    /// Called when a memory block is created.
    /// </summary>
    void OnMemoryBlockCreated(int blockSize);

    /// <summary>
    /// Called when a memory block is destructed.
    /// If this method is ever called, there are some improperly disposed resources at the application level.
    /// </summary>
    void OnMemoryBlockDestructed(int blockSize);

    /// <summary>
    /// Called when a memory block is resurrected
    /// Happens if thread that was used to send data is destroyed and a memory block had only one lease
    /// </summary>
    void OnMemoryBlockResurrected(int blockSize);

    /// <summary>
    /// Called when command successfully executed (response received)
    /// </summary>
    void OnCommandExecuted(IRespClient client, int elapsedTimeInMs, string command);

    /// <summary>
    /// Called when command execution fails
    /// </summary>
    void OnCommandFailed(IRespClient client, int elapsedTimeInMs, string command);

    /// <summary>
    /// Called when command time-outs
    /// </summary>
    void OnCommandTimedOut(IRespClient client, int elapsedTimeInMs, string command);

    /// <summary>
    /// Called when command cancelled
    /// </summary>
    void OnCommandCancelled(IRespClient client, int elapsedTimeInMs, string command);
}