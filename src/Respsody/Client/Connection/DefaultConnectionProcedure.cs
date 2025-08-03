using System.Net.Sockets;
using Respsody.Client.Connection.Options;
using Respsody.Network;
using Respsody.Resp;

namespace Respsody.Client.Connection;

public class DefaultConnectionProcedure(ConnectionOptions connectionOptions)
    : IConnectionProcedure
{
    private const int SupportedProtocolVersion = 3;

    public IReadOnlyDictionary<string, string?> Config { get; } = new Dictionary<string, string?>
    {
        { nameof(connectionOptions.ClientName), connectionOptions.ClientName },
        { nameof(connectionOptions.Endpoint), connectionOptions.Endpoint },
        { nameof(connectionOptions.ConnectionTimeoutMs), connectionOptions.ConnectionTimeoutMs.ToString() }
    };

    public TimeSpan Timeout => TimeSpan.FromMilliseconds(connectionOptions.ConnectionTimeoutMs);

    public async Task<ConnectedSocket> Connect(CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (Timeout > TimeSpan.Zero)
            cts.CancelAfter(Timeout);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        var endPoint = connectionOptions.GetEndPoint();
        await socket.ConnectAsync(endPoint, cts.Token);
        using var socketRpc = new DirectSocketRpc(socket);
        var authOptions = connectionOptions.AuthOptions;

        using var helloReply = (
            await socketRpc.Rpc(
                "HELLO",
                c =>
                {
                    c.Arg(SupportedProtocolVersion);

                    if (authOptions is { } && !string.IsNullOrWhiteSpace(authOptions.Password))
                    {
                        c.Token("AUTH");
                        c.Arg(authOptions.UserName ?? "default");
                        c.Arg(authOptions.Password);
                    }

                    if (connectionOptions.ClientName is { } clientName)
                    {
                        c.Token("SETNAME");
                        c.Arg(clientName);
                    }
                },
                token)
        ).ToRespMap();

        return new ConnectedSocket(socket, new ConnectionMetadata(endPoint, helloReply.ToMapWithStringKey()));
    }
}