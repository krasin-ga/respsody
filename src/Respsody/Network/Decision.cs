namespace Respsody.Network;

public readonly struct Decision
{
    public ushort Offset { get; }
    public int Length { get; }
    internal Mode SelectedMode { get; }

    private Decision(ushort offset, int length, Mode mode)
    {
        Offset = offset;
        Length = length;
        SelectedMode = mode;
    }

    public static Decision PredefinedLength(int length)
        => new(0, length, Mode.Length);

    public static Decision PredefinedLength(ushort examined, int remainingLength)
        => new(examined, remainingLength, Mode.Length);

    public static Decision MarkBoundary(ushort offset)
        => new(offset, 0, mode: Mode.MarkBoundary);

    internal enum Mode : byte
    {
        None = 0,
        Length = 1,
        MarkBoundary = 2
    }
}