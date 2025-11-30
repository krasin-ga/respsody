using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Library.Disposables;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespResponse(
    Frame<RespContext>? frame,
    RespAggregate? aggregate,
    RespAggregate? attribute,
    CompletionGuard guard)
    : IRespResponse
{
    public RespValueVariant? Variant { get; } = frame.HasValue
        ? new RespValueVariant(frame.Value)
        : aggregate is { }
            ? new RespValueVariant(aggregate)
            : null;

    public RespAggregate? Attribute { get; } = attribute;

    public void Dispose()
    {
        if (!guard.CanDispose())
            return;

        Variant?.Dispose();
        Attribute?.Dispose();
    }

    public (T, IDisposable Liftime) AsExternallyOwnedUnsafe<T>() where T : IRespResponse
    {
        var disposable = new CompositeDisposable();
        var frame = Variant?.Simple;
        if (frame.HasValue)
        {
            var (ownedFrame, lifetime) = frame.Value.AsExternallyOwned();
            disposable.Add(lifetime);
            frame = ownedFrame;
        }

        if (Variant?.Aggregate is { } agg)
            disposable.Add(agg.AsExternallyOwned());

        if (Attribute is { })
            disposable.Add(Attribute.AsExternallyOwned());

        var ownedResponse = new RespResponse(frame, Variant?.Aggregate, Attribute, guard);

        return (Unsafe.As<RespResponse, T>(ref ownedResponse), disposable);
    }

    public string ToDebugString()
    {
        if (Variant is { } variant)
            return variant.ToDebugString();

        if (Attribute is { } att)
            return att.ToDebugString();

        return string.Empty;
    }
}