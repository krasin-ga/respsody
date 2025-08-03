using System.Buffers;
using Respsody.Memory;
using Respsody.Network;

namespace Respsody.Resp.Parsing;

public sealed class RespFraming(
    MemoryBlocks memoryBlocks,
    int receivingBlockSize,
    ArrayPool<byte>? arrayPool = null)
    : Framing<RespContext>(new WindowSize(1, 128), memoryBlocks, receivingBlockSize, arrayPool)
{
    private readonly CrLfParser _crLfParser = new();
    private readonly LengthParser _lengthParser = new();
    private State _currentState;

    protected override Sequence<RespContext> Create()
    {
        _currentState = State.WaitingForType;

        return new Sequence<RespContext>
        {
            Context = new RespContext()
        };
    }

    protected override Decision Advance(
        ref Sequence<RespContext> sequence,
        ReadOnlySpan<byte> window)
    {
        var examined = 0;

        if (_currentState is State.WaitingForType)
        {
            var respType = (RespType)window[0];
            sequence.Context.Type = respType;
            window = window[++examined..];

            _currentState = respType.IsLengthPrefixed()
                ? State.ParsingLength
                : State.WaitingForCrLf;

            sequence.Context.DataOffset += examined;
        }

        if (_currentState is State.ParsingLength)
        {
            if (_lengthParser.FeedAndTryParse(window, out var result))
            {
                sequence.Context.Length = result.Parsed;
                examined += result.Examined;
                sequence.Context.DataOffset += result.Examined;

                if (!sequence.Context.Type.IsCollection()
                    && (result.Parsed > 0 || sequence.Context.Type != RespType.SteamedStringChunk && result.Parsed == 0))
                {
                    sequence.Context.DataOffset += 2;
                    return Decision.PredefinedLength((ushort)examined, result.Parsed + 4);
                }

                window = window[result.Examined..];
                _currentState = State.WaitingForCrLf;
            }
            else
            {
                sequence.Context.DataOffset += window.Length;
                return default;
            }
        }

        if (_currentState is State.WaitingForCrLf)
            if (_crLfParser.TryFind(window, out var offset))
            {
                //sequence.Context.DataOffset += 2;
                return Decision.MarkBoundary((ushort)(examined + offset));
            }

        return default;
    }

    private enum State : byte
    {
        WaitingForType = 0,
        ParsingLength = 1,
        WaitingForCrLf = 2
    }
}