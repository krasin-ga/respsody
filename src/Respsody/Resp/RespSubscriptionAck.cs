namespace Respsody.Resp;

/// <summary>
/// Used for both subscribe and unsubscribe acknowledgements
/// </summary>
public sealed class RespSubscriptionAck(Bytes[] acks) : IRespResponse
{
    /// <summary>
    /// Channels or pattern
    /// </summary>
    public Bytes[] Acks { get; } = acks;

    public void Dispose()
    {
    }

    public (T, IDisposable Liftime) AsExternallyOwnedUnsafe<T>() where T : IRespResponse
    {
        throw new NotSupportedException();
    }
}