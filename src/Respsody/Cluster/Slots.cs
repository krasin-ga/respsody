using System.Text;

namespace Respsody.Cluster;

public record Slots(SlotRange[] Ranges)
{
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        if (Ranges.Length == 0)
        {
            builder.Append("[]");
            return false;
        }

        foreach (var slotRange in Ranges)
        {
            slotRange.WriteToStringBuilder(builder);
            builder.Append(", ");
        }

        builder.Length -= 2;
        return true;
    }
}