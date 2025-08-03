using System.Net.Sockets;

namespace Respsody.Network;

public class StructuredSocketOptions
{
    public SocketFlags SendingFlags { get; set; } = SocketFlags.None;
    public SocketFlags ReceivingFlags { get; set; } = SocketFlags.None;
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int OutgoingBufferSize { get; set; } = 512*1024;
}