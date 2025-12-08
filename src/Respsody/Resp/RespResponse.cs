using System.Runtime.CompilerServices;
using Respsody.Client;
using Respsody.Library.Disposables;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct RespResponse(
    Frame<RespContext>? frame,
    RespAggregate? aggregate,
    RespAggregate? attribute,
    DisposalGuard guard)
    : IRespResponse
{
    private RespValueVariant InternalVariant { get; } = frame.HasValue
        ? new RespValueVariant(frame.Value)
        : aggregate is not null
            ? new RespValueVariant(aggregate)
            : throw new InvalidOperationException();

    public OwnedRespValueVariant Variant => new(InternalVariant, guard);

    internal RespAggregate? Attribute { get; } = attribute;

    public void Dispose()
    {
        if (!guard.TryDispose())
            return;

        InternalVariant.Dispose();
        Attribute?.Dispose();
    }

    public (T, IDisposable Liftime) AsExternallyOwnedUnsafe<T>() where T : IRespResponse
    {
        var disposable = new CompositeDisposable();
        var frame = InternalVariant.Simple;
        if (frame.HasValue)
        {
            var (ownedFrame, lifetime) = frame.Value.AsExternallyOwned();
            disposable.Add(lifetime);
            frame = ownedFrame;
        }

        if (InternalVariant.Aggregate is { } agg)
            disposable.Add(agg.AsExternallyOwned());

        if (Attribute is { })
            disposable.Add(Attribute.AsExternallyOwned());

        var ownedResponse = new RespResponse(frame, InternalVariant.Aggregate, Attribute, guard.ToCheckOnly());

        return (Unsafe.As<RespResponse, T>(ref ownedResponse), disposable);
    }

    public string ToDebugString()
    {
        var attributeDbgString = Attribute?.ToDebugString();

        return attributeDbgString == null 
            ? InternalVariant.ToDebugString()
            : $"{InternalVariant.ToDebugString()} [{attributeDbgString}]";
    }
}