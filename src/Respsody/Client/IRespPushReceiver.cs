using Respsody.Resp;

namespace Respsody.Client;

public interface IRespPushReceiver
{
    /// <summary>
    /// Implementation of this method must ensure that RespPush structure is properly disposed
    /// </summary>
    void Receive(RespPush push);
}