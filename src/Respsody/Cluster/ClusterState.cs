namespace Respsody.Cluster;

public class ClusterState(ClusterNode[] nodes, SlotsMap map)
{
    public IReadOnlyList<ClusterNode> Nodes => nodes;
    internal ClusterNode[] PrimaryNodes { get; } = nodes.Where(node => node.Role == Role.Primary).ToArray();
    public SlotsMap Map => map;

}
