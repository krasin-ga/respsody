namespace Respsody.Cluster;

public record SpecialSlotState(SpecialSlotState.Kind StateKind, string? Node)
{
    public enum Kind
    {
        None = 0,
        Importing,
        Migrating
    }

    public static readonly SpecialSlotState None = new(Kind.None, null);
}