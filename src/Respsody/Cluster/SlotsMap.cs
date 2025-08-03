using Respsody.Cluster.Errors;
using System.Diagnostics.CodeAnalysis;

namespace Respsody.Cluster;

public class SlotsMap
{
    private const int MaxSlots = 16384;
    private readonly Dictionary<string, ClusterNode> _endpointToNode;
    private readonly Dictionary<string, ClusterNode> _endpointToPrimaryNode;
    private readonly ClusterNode[] _slotToNode;
    public bool IsUnstable { get; }

    public SlotsMap(IEnumerable<ClusterNode> nodes)
    {
        _slotToNode = new ClusterNode[MaxSlots];
        _endpointToPrimaryNode = [];
        _endpointToNode = [];
        foreach (var clusterNode in nodes)
        {
            if (clusterNode.Metadata.Address is { } address)
            {
                var endpoint = address.FormatEndpoint();

                if (clusterNode.Metadata.Role == Role.Primary)
                    _endpointToPrimaryNode[endpoint] = clusterNode;

                _endpointToNode[endpoint] = clusterNode;
            }

            if (clusterNode.Metadata.Role != Role.Primary)
                continue;

            var nodeMetadata = clusterNode.Metadata;
            foreach (var range in nodeMetadata.Slots.Ranges)
            {
                if (range.State.StateKind != SpecialSlotState.Kind.None)
                {
                    IsUnstable = true;
                    continue;
                }

                foreach (var slot in range.GetSlots())
                    _slotToNode[slot] = clusterNode;
            }
        }
    }

    public ClusterNode GetNode(ushort slot) => _slotToNode[slot];

    public bool TryGetNodeByEndpoint(ReadOnlySpan<char> endpoint, [NotNullWhen(true)] out ClusterNode? clusterNode)
    {
        //todo: use alternate lookup
        return _endpointToNode.TryGetValue(endpoint.ToString(), out clusterNode);
    }

    public void Update(SlotMovedError slotMovedError)
    {
        if (_endpointToPrimaryNode.TryGetValue(slotMovedError.EndPoint.ToString(), out var node))
            _slotToNode[slotMovedError.Slot] = node;
    }
}