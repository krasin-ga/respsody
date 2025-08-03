namespace Respsody.Network;

public interface IStructuredSocketHandler<TIncoming>
{
    ValueTask InitializeConnection(ConnectedSocket connectedSocket, int generation, CancellationToken cancellationToken);
    ValueTask OnConnected(int generation, CancellationToken cancellationToken);
    void HandleIncoming(ReadyFrames<TIncoming> readyFrames);
    void OnDisconnected(Exception? exception, int generation);
    void HandleConnectionError(Exception exception);
}