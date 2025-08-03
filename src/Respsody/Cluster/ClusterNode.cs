using Respsody.Client;
using Respsody.Exceptions;

namespace Respsody.Cluster;

public class ClusterNode(RespClient client, NodeMetadata metadata)
{
    private readonly List<ClusterNode> _replicas = [];
    private int _pointer;
    public RespClient Client { get; } = client;
    public NodeMetadata Metadata { get; } = metadata;
    public Role Role => Metadata.Role;
    public string Id => Metadata.Id;

    public IReadOnlyList<ClusterNode> Replicas => _replicas;

    public void AssignReplica(ClusterNode clusterNode)
    {
        if (clusterNode.Role == Role.Primary)
            throw new RespUnexpectedOperationException("Cannot assign master as replica");

        if (Role != Role.Primary)
            throw new RespUnexpectedOperationException("Cannot assign replica to replica");

        if (_replicas.Any(r => r.Id == clusterNode.Id))
            return;

        _replicas.Add(clusterNode);
    }

    public ClusterNode? PickReplica()
    {
        var replicasCount = Replicas.Count;

        return replicasCount switch
        {
            0 => null,
            1 => _replicas[0],
            _ => _replicas[Interlocked.Increment(ref _pointer) % replicasCount]
        };
    }

    public void AssignReplicas(ClusterNode[] clusterNodes)
    {
        foreach (var clusterNode in clusterNodes)
            if (clusterNode.Role == Role.Replica && clusterNode.Metadata.PrimaryId == Id)
                AssignReplica(clusterNode);
    }

    public RespClient PickAny()
    {
        var nodeCount = _replicas.Count + 1;

        var index = Interlocked.Increment(ref _pointer) % nodeCount;

        return index == 0 ? Client : _replicas[index - 1].Client;
    }
}