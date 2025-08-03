using System.Net.Sockets;

namespace Respsody.Network;

public record ConnectedSocket(Socket Socket, ConnectionMetadata Metadata);