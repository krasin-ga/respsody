using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Xunit;

namespace Respsody.Tests;

public class ConnectionFailureTests
{
    [Fact]
    public async Task FailedHandshakeClosesItsSocket()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var connection = new DefaultConnectionProcedure(new ConnectionOptions
        {
            Endpoint = listener.LocalEndPoint!.ToString()!, ClientName = null
        });
        var connecting = connection.Connect(deadline.Token);
        using var server = await listener.AcceptAsync(deadline.Token);
        var request = new byte[22];
        for (var offset = 0; offset < request.Length;)
            offset += await server.ReceiveAsync(request.AsMemory(offset), SocketFlags.None, deadline.Token);
        Assert.Equal("*2\r\n$5\r\nHELLO\r\n$1\r\n3\r\n"u8.ToArray(), request);
        await server.SendAsync("-ERR denied\r\n"u8.ToArray(), SocketFlags.None, deadline.Token);

        await Assert.ThrowsAnyAsync<Exception>(() => connecting);
        Assert.Equal(0, await server.ReceiveAsync(new byte[1], SocketFlags.None, deadline.Token));
    }
}
