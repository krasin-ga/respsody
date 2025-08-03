using System.Runtime.CompilerServices;
using Respsody.Library.Disposables;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly record struct RespResponse(
    Frame<RespContext>? Frame,
    RespAggregate? Aggregate,
    RespAggregate? Attribute)
    : IRespResponse
{
    public void Dispose()
    {
        Frame?.Dispose();
        Aggregate?.Dispose();
        Attribute?.Dispose();
    }

    public (T, IDisposable Liftime) AsExternallyOwnedUnsafe<T>() where T : IRespResponse
    {
        var disposable = new CompositeDisposable();
        var frame = Frame;
        if (frame.HasValue)
        {
            var (ownedFrame, lifetime) = frame.Value.AsExternallyOwned();
            disposable.Add(lifetime);
            frame = ownedFrame;
        }

        if (Aggregate is { })
            disposable.Add(Aggregate.AsExternallyOwned());

        if (Attribute is { })
            disposable.Add(Attribute.AsExternallyOwned());

        var ownedResponse = this with { Frame = frame };

        return (Unsafe.As<RespResponse, T>(ref ownedResponse), disposable);
    }

    public string ToDebugString()
    {
        if (Frame is { } frame)
            return frame.ToDebugString();

        if (Aggregate is { } agg)
            return agg.ToDebugString();

        if (Attribute is { } att)
            return att.ToDebugString();

        return string.Empty;
    }
}