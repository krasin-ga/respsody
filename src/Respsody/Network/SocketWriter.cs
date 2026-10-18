using System.Net.Sockets;

namespace Respsody.Network;

internal static class SocketWriter
{
    internal static async ValueTask SendAllAsync(Socket socket, ReadOnlyMemory<byte> bytes,
        SocketFlags flags, CancellationToken token)
    {
        while (!bytes.IsEmpty)
        {
            var sent = await socket.SendAsync(bytes, flags, token);
            if (sent == 0)
                throw new IOException("Socket closed before the send completed.");
            bytes = bytes[sent..];
        }
    }

    internal static async ValueTask SendAllAsync(Socket socket, IList<ArraySegment<byte>> segments,
        SocketFlags flags)
    {
        if (segments.Count == 0)
            return;

        var sent = await socket.SendAsync(segments, flags);
        if (sent == 0)
            throw new IOException("Socket closed before the send completed.");

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (sent >= segment.Count)
            {
                sent -= segment.Count;
                continue;
            }

            await SendAllAsync(socket, segment.AsMemory(sent), flags, CancellationToken.None);
            sent = 0;
        }
    }
}
