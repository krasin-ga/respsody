using System;
using Respsody.Cluster;
using Xunit;

namespace Respsody.Tests;

public class ClusterTopologyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("node0")]
    [InlineData("node0 127.0.0.1:7000@17000 master - 0 0 1")]
    public void IncompleteNodeEntryHasUsefulFormatError(string line)
    {
        var exception = Assert.Throws<FormatException>(() => NodeMetadata.Parse(line));
        Assert.Contains("eight fields", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("16384")]
    [InlineData("65535")]
    [InlineData("10-9")]
    [InlineData("0-16384")]
    [InlineData("1-2-3")]
    [InlineData("[1->-node")]
    [InlineData("[1->-]")]
    [InlineData("[1-2->-node]")]
    public void InvalidSlotRangeHasFormatError(string range)
    {
        Assert.Throws<FormatException>(() => SlotRange.Parse(range));
    }

    [Theory]
    [InlineData("0", 0, 0, SpecialSlotState.Kind.None)]
    [InlineData("0-16383", 0, 16383, SpecialSlotState.Kind.None)]
    [InlineData("[16383->-node]", 16383, 16383, SpecialSlotState.Kind.Migrating)]
    [InlineData("[0-<-node]", 0, 0, SpecialSlotState.Kind.Importing)]
    public void ValidSlotRangesPreserveBoundsAndState(string text, int from, int to, SpecialSlotState.Kind state)
    {
        var range = SlotRange.Parse(text);
        Assert.Equal(from, range.From);
        Assert.Equal(to, range.To);
        Assert.Equal(state, range.State.StateKind);
    }

    [Fact]
    public void ConflictingPrimaryAssignmentsAreRejected()
    {
        var first = ClusterNodeTests.CreateNode(0, "0-100");
        using var client = first.Client;
        var second = new ClusterNode(client, first.Metadata with { Id = "node1", Slots = new Slots([SlotRange.Parse("100-200")]) });

        var exception = Assert.Throws<FormatException>(() => new SlotsMap([first, second]));
        Assert.Contains("100", exception.Message);
    }

    [Theory]
    [InlineData(10, 9)]
    [InlineData(0, 16384)]
    public void ManuallyConstructedInvalidRangesAreRejected(int from, int to)
    {
        var original = ClusterNodeTests.CreateNode(0);
        using var client = original.Client;
        var node = new ClusterNode(client, original.Metadata with
        {
            Slots = new Slots([new SlotRange((ushort)from, (ushort)to, SpecialSlotState.None)])
        });

        Assert.Throws<FormatException>(() => new SlotsMap([node]));
    }
}
