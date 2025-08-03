using System.Runtime.CompilerServices;

namespace Respsody.Network;

public readonly record struct WindowSize(int Min, int Max)
{
    public WindowSize(int fixedSize)
        : this(fixedSize, fixedSize)
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> SliceBlock(ReadOnlySpan<byte> block, int position)
    {
        return position + Max > block.Length
            ? block[position..]
            : block.Slice(position, Max);
    }
}