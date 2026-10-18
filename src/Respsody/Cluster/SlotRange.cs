using System.Text;

namespace Respsody.Cluster;

public readonly record struct SlotRange(ushort From, ushort To, SpecialSlotState State)
{
    public SpecialSlotState State { get; } = State;

    public SlotEnumerator GetSlots() => new(From, To);

    public static SlotRange Parse(string str)
    {
        var state = SpecialSlotState.None;
        var hasSpecialState = str.StartsWith('[');
        if (hasSpecialState)
        {
            if (!str.EndsWith(']'))
                throw new FormatException($"Unterminated cluster slot state: {str}");
            str = str[1..^1];

            var importing = str.Split("-<-");
            var migrating = str.Split("->-");
            if (importing.Length == 2 && importing[1].Length > 0)
            {
                str = importing[0];
                state = new SpecialSlotState(SpecialSlotState.Kind.Importing, importing[1]);
            }
            else if (migrating.Length == 2 && migrating[1].Length > 0)
            {
                str = migrating[0];
                state = new SpecialSlotState(SpecialSlotState.Kind.Migrating, migrating[1]);
            }
            else
            {
                throw new FormatException($"Invalid cluster slot state: {str}");
            }
        }

        var split = str.Split('-');
        if (split.Length is < 1 or > 2 || hasSpecialState && split.Length != 1
            || !ushort.TryParse(split[0], out var from) || from >= 16384)
            throw new FormatException($"Invalid cluster slot range: {str}");

        var to = from;
        if (split.Length == 2 && (!ushort.TryParse(split[1], out to) || to >= 16384 || to < from))
            throw new FormatException($"Invalid cluster slot range: {str}");

        return new SlotRange(from, to, state);
    }

    private bool PrintMembers(StringBuilder builder)
    {
        return WriteToStringBuilder(builder);
    }

    public bool WriteToStringBuilder(StringBuilder builder)
    {
        if (State != SpecialSlotState.None)
            builder.Append($"({State}) ");

        if (From == To)
        {
            builder.Append(From);
            return true;
        }

        builder.Append(From).Append('-').Append(To);

        return true;
    }

    public struct SlotEnumerator(ushort from, ushort to)
    {
        public ushort Current { get; private set; } = (ushort)(from - 1);

        public bool MoveNext()
        {
            if (Current >= to && Current != ushort.MaxValue)
                return false;

            Current++;
            return true;
        }

        public readonly SlotEnumerator GetEnumerator() => this;
    }
}
