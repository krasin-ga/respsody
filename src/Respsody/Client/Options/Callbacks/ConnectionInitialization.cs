using Respsody.Network;

namespace Respsody.Client.Options.Callbacks;

public delegate ValueTask ConnectionInitialization(ConnectedSocket connectedSocket, CancellationToken cancellationToken);