namespace Respsody.Cluster;

public class ClusterState(ClusterNode[] nodes, SlotsMap map)
{
    public IReadOnlyList<ClusterNode> Nodes => nodes;
    public SlotsMap Map => map;

}