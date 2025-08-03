using Respsody.Exceptions;

namespace Respsody.Cluster.Errors;

public readonly ref struct AskRedirectionError(ushort slot, ReadOnlySpan<char> endPoint)
{
    public ushort Slot { get; } = slot;
    public ReadOnlySpan<char> EndPoint { get; } = endPoint;

    public static bool TryParse(RespErrorResponseException exception, out AskRedirectionError error)
    {
        const string moved = "ASK ";
        if (!exception.Error.StartsWith(moved, StringComparison.Ordinal))
        {
            error = default;
            return false;
        }

        Span<Range> ranges = stackalloc Range[2];
        var remainingSpan = exception.Error.AsSpan(moved.Length);

        var count = remainingSpan.Split(ranges, ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (count == 0)
        {
            error = default;
            return false;
        }

        var slot = ushort.Parse(remainingSpan[ranges[0]]);

        if (count == 1)
        {
            error = new AskRedirectionError(slot, []);
            return true;
        }

        error = new AskRedirectionError(slot, remainingSpan[ranges[1]]);
        return true;
    }
}