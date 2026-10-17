using System;
using System.Linq;
using System.Reflection;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Cluster;
using Xunit;

namespace Respsody.Tests;

public class ClusterNodeTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SelectionRemainsValidWhenCounterWraps(int replicaCount)
    {
        var nodes = Enumerable.Range(0, replicaCount + 1).Select(index => CreateNode(index)).ToArray();
        try
        {
            var primary = nodes[0];
            primary.AssignReplicas(nodes);
            foreach (var start in new[] { int.MaxValue - 1, int.MinValue, -2 })
            {
                SetPointer(primary, start);
                for (var offset = 1; offset <= replicaCount * 2; offset++)
                {
                    var index = (uint)unchecked(start + offset) % (uint)replicaCount;
                    Assert.Same(nodes[index + 1], primary.PickReplica());
                }

                SetPointer(primary, start);
                for (var offset = 1; offset <= nodes.Length * 2; offset++)
                {
                    var index = (uint)unchecked(start + offset) % (uint)nodes.Length;
                    Assert.Same(nodes[index].Client, primary.PickAny());
                }
            }
        }
        finally
        {
            foreach (var node in nodes)
                node.Client.Dispose();
        }
    }

    private static void SetPointer(ClusterNode node, int value) =>
        typeof(ClusterNode).GetField("_pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(node, value);

    internal static ClusterNode CreateNode(int index, string slots = "0-16383")
    {
        var client = new RespClient(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions
        {
            Endpoint = "127.0.0.1:1"
        }));
        var metadata = NodeMetadata.Parse(index == 0
            ? $"node0 127.0.0.1:7000@17000 master - 0 0 1 connected {slots}".TrimEnd()
            : $"node{index} 127.0.0.1:{7000 + index}@{17000 + index} slave node0 0 0 1 connected");
        return new ClusterNode(client, metadata);
    }
}
