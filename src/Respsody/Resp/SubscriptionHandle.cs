namespace Respsody.Resp;

public record SubscriptionHandle(string Command, Bytes Ack)
{
    public bool IsNull { get; init; }
}
