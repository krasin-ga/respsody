namespace Respsody.Resp;

/// <summary>
/// Response marker interface
/// </summary>
public interface IRespResponse : IDisposable
{
    //make internal
    internal (T, IDisposable Liftime) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse;
}