using System.Runtime.CompilerServices;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespVoid : IRespResponse
{
    public void Dispose()
    {
    }

    public (T, IDisposable) AsExternallyOwnedUnsafe<T>()
        where T : IRespResponse
    {
        var @this = this;
        return (Unsafe.As<RespVoid, T>(ref @this), EmptyDisposable.Instance);
    }
}