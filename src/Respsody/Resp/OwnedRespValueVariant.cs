using Respsody.Client;

namespace Respsody.Resp;

public readonly struct OwnedRespValueVariant(RespValueVariant variant, DisposalGuard guard)
{
    internal RespValueVariant Variant { get; } = variant;
    internal DisposalGuard Guard { get; } = guard.ToCheckOnly();
}