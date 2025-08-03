using System.Runtime.CompilerServices;
using Respsody.Library;

namespace Respsody.Resp.Parsing;

public class CrLfParser
{
    private State _state;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool TryFind(ReadOnlySpan<byte> span, out ushort offset)
    {
        for (ushort i = 0; i < span.Length; i++)
        {
            var value = span[i];
            switch (value)
            {
                case Constants.CR when _state == State.WaitingForCr:
                    _state = State.WaitingForLf;
                    break;
                case Constants.LF when _state == State.WaitingForLf:
                    _state = State.WaitingForCr;
                    offset = i;
                    return true;
                default:
                    if (_state == State.WaitingForCr)
                        continue;
                    throw new InvalidOperationException($"Invalid state: got `{(char)value}` ({value}) on {_state}");
            }
        }

        offset = 0;
        return false;
    }

    private enum State : byte
    {
        WaitingForCr = 0,
        WaitingForLf = 1
    }
}