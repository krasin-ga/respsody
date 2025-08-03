using Respsody.Resp;

namespace Respsody.Client;

public class NoOpRespPushReceiver : IRespPushReceiver
{
    public void Receive(RespPush push)
    {
        push.Dispose();
    }
}