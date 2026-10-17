using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Network;
using Xunit;

namespace Respsody.Tests;

public class SocketWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendsCompletePayloadThroughSmallSocketBuffers(bool scatter)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var sender = new Socket(SocketType.Stream, ProtocolType.Tcp) { SendBufferSize = 4096 };
        await sender.ConnectAsync(listener.LocalEndPoint!, deadline.Token);
        using var receiver = await listener.AcceptAsync(deadline.Token);
        var bytes = new byte[2 * 1024 * 1024 + 17];
        new Random(1234).NextBytes(bytes);
        var segments = new List<ArraySegment<byte>>
        {
            new(bytes, 0, 65537), new(bytes, 65537, 997), new(bytes, 66534, bytes.Length - 66534)
        };
        var reading = ReadAll(receiver, bytes.Length, deadline.Token);
        if (scatter)
            await SocketWriter.SendAllAsync(sender, segments, SocketFlags.None).AsTask().WaitAsync(deadline.Token);
        else
            await SocketWriter.SendAllAsync(sender, bytes.AsMemory(), SocketFlags.None, deadline.Token);

        Assert.Equal(bytes, await reading);
        Assert.Equal(65537, segments[1].Offset);
        Assert.Equal(997, segments[1].Count);
    }

    private static async Task<byte[]> ReadAll(Socket socket, int length, CancellationToken token)
    {
        var bytes = new byte[length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await socket.ReceiveAsync(bytes.AsMemory(offset, Math.Min(8192, length - offset)),
                SocketFlags.None, token);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }
}
