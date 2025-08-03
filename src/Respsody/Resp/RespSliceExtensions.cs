using System.Runtime.CompilerServices;
using Respsody.Memory;

namespace Respsody.Resp;

public static class RespSliceExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RespType GetRespType(this Frame<RespContext> frame)
    {
        return frame.Context.Type;
    }
}