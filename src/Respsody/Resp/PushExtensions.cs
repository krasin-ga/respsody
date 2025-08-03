
using System.Diagnostics.CodeAnalysis;

namespace Respsody.Resp;

public static class PushExtensions
{
    private static readonly byte[] Subscribe = "subscribe"u8.ToArray();

    public static bool TryGetSubscription(this RespPush respPush, [NotNullWhen(true)]out SubscriptionHandle? handle)
    {
        var kind = respPush[0].ToRespString().GetSpan();

        if (kind.EndsWith(Subscribe))
        {
            handle = new SubscriptionHandle(
                DefaultRespEncoding.Value.GetString(kind),
                new Bytes(respPush[1].ToRespString().GetSpan().ToArray()));

            return true;
        }

        handle = null;
        return false;
    }

    public static RespString GetMessageKind(this RespPush respPush)
    {
        return respPush[0].ToRespString();
    }

    public static RespString GetChannel(this RespPush respPush)
    {
        return respPush[1].ToRespString();
    }

    public static RespString GetPushMessage(this RespPush respPush)
    {
        return respPush[2].ToRespString();
    }
}