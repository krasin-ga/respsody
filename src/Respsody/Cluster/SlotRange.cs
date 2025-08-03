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
            str = str.Trim('[', ']');

            var importing = str.Split("-<-");
            var migrating = str.Split("->-");
            if (importing.Length == 2)
            {
                str = importing[0];
                state = new SpecialSlotState(SpecialSlotState.Kind.Importing, importing[1]);
            }
            else if (migrating.Length == 2)
            {
                str = migrating[0];
                state = new SpecialSlotState(SpecialSlotState.Kind.Migrating, migrating[1]);
            }
            else
            {
                throw new InvalidOperationException($"Failed to parse special state for string: {str}");
            }
        }

        var split = str.Split('-');
        if (split.Length == 1)
        {
            var slot = ushort.Parse(split[0]);
            return new SlotRange(slot, slot, state);
        }

        var from = ushort.Parse(split[0]);
        var to = ushort.Parse(split[1]);

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