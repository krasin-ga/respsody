using Respsody.Cluster;
using Respsody.Cluster.Errors;
using Respsody.Exceptions;
using Xunit;

namespace Respsody.Tests;

public class ClusterSlotsMapTests
{
    [Fact]
    public void MissingSlotThrowsRoutingErrorAndMovedCanFillIt()
    {
        var node = ClusterNodeTests.CreateNode(0, "0-100");
        using var client = node.Client;
        var map = new SlotsMap([node]);

        Assert.Same(node, map.GetNode(0));
        Assert.Same(node, map.GetNode(100));
        var error = Assert.Throws<RespClusterCommandExecutionException>(() => map.GetNode(101));
        Assert.Contains("101", error.Message);

        map.Update(new SlotMovedError(101, "127.0.0.1:7000"));
        Assert.Same(node, map.GetNode(101));
        Assert.Throws<RespClusterCommandExecutionException>(() => map.GetNode(102));
    }

    [Fact]
    public void NewTopologyDoesNotRetainRemovedSlotAssignments()
    {
        var oldNode = ClusterNodeTests.CreateNode(0);
        var newNode = ClusterNodeTests.CreateNode(0, "100-16383");
        using var oldClient = oldNode.Client;
        using var newClient = newNode.Client;
        var oldMap = new SlotsMap([oldNode]);
        var newMap = new SlotsMap([newNode]);

        Assert.Same(oldNode, oldMap.GetNode(0));
        Assert.Throws<RespClusterCommandExecutionException>(() => newMap.GetNode(0));
        Assert.Same(newNode, newMap.GetNode(100));
        Assert.Same(newNode, newMap.GetNode(16383));
    }
}
