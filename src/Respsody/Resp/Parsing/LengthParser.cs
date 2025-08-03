using System.Runtime.CompilerServices;
using Respsody.Library;

namespace Respsody.Resp.Parsing;

public class LengthParser
{
    private const byte Zero = (byte)'0';
    private int _accumulated = -1;
    private int _sign = 1;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool FeedAndTryParse(
        ReadOnlySpan<byte> span,
        out (int Parsed, int Examined) result)
    {
        if (span.Length == 0)
        {
            result = default;
            return false;
        }

        if (_accumulated == -1)
        {
            var @byte = span[0];
            if (@byte == '?')
            {
                result = (Constants.StreamedLength, 1);
                return true;
            }

            if (@byte == '-')
            {
                _sign = -1;
                span = span[1..];
            }

            _accumulated = 0;
        }

        var i = 0;
        for (; i < span.Length; i++)
        {
            var c = span[i];
            if (c == Constants.CR)
            {
                result = (_accumulated * _sign, i);
                _accumulated = -1;
                _sign = 1;

                return true;
            }

            _accumulated = _accumulated * 10 + (span[i] - Zero);
        }

        result = (0, i);
        return false;
    }
}