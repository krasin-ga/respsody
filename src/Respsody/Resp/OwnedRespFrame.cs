using Respsody.Client;
using Respsody.Memory;

namespace Respsody.Resp;

public readonly struct OwnedRespFrame(Frame<RespContext> frame, DisposalGuard guard)
{
    internal Frame<RespContext> Frame { get; } = frame;
    internal DisposalGuard Guard { get; } = guard.ToCheckOnly();
}