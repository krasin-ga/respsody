using System.Diagnostics;
using System.Text;
using Respsody.Client;
using Respsody.Cluster.Errors;
using Respsody.Cluster.Options;
using Respsody.Exceptions;
using Respsody.Library.Disposables;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;

namespace Respsody.Cluster;

public sealed class ClusterRouter
{
    private readonly RoleRouter _anyRouter;
    private readonly MemoryBlocks _memoryBlocks;
    private readonly ClusterRouterOptions _options;
    private readonly RoleRouter _preferReplicaRouter;
    private readonly RoleRouter _primaryRouter;
    private readonly RoleRouter _replicaRouter;
    private ClusterState? _state;
    private int _isSyncing;
    private int _outgoingBlockSize;
    public ClusterRouter(ClusterRouterOptions options)
    {
        _options = options;
        _memoryBlocks = options.ClientOptions.MemoryBlocks;
        _anyRouter = new RoleRouter(RolePreference.Any, this);
        _primaryRouter = new RoleRouter(RolePreference.Primary, this);
        _preferReplicaRouter = new RoleRouter(RolePreference.PreferReplica, this);
        _replicaRouter = new RoleRouter(RolePreference.Replica, this);
        _outgoingBlockSize = options.ClientOptions.OutgoingMemoryBlockSize;
    }

    public async Task Initialize()
    {
        await UpdateClusterState(_options.SeedEndpoints);
    }

    private async Task UpdateClusterState(IEnumerable<string> endpoints)
    {
        foreach (var seedEndpoint in endpoints)
        {
            await UpdateClusterStateFromSeed(seedEndpoint);
            break;
        }

        if (_state is { })
            _ = Task.WhenAll(_state.Nodes.Select(n => n.Client.Connect()));
    }

    public async Task SafeSyncClusterState()
    {
        if(Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
            return;

        using Defer _ = new(() => _isSyncing = 0);

        var endpoints = (_state?.Nodes.Select(n => n.Metadata.Address?.FormatEndpoint()) ?? [])
            .Concat(_options.SeedEndpoints).Where(e => !string.IsNullOrWhiteSpace(e));

        await UpdateClusterState(endpoints!);
    }

    private async Task UpdateClusterStateFromSeed(string seedEndpoint)
    {
        var currentState = _state;

        var client = CreateClient(seedEndpoint);
        await client.Connect();

        var connectionMetadata = new ClusterNodeConnectionMetadata(client.Metadata!);
        if (!connectionMetadata.ServerMode.IsCluster)
            throw new RespConnectionException("Seed node's mode must be `cluster`");

        using var nodesCommand = client.CreateCommand<RespString>("CLUSTER").Arg("NODES");
        using var response = await client.ExecuteCommand(nodesCommand);
        var responseString = response.ToString() ?? throw new RespUnexpectedOperationException(
            "Failed to get cluster nodes because response is null");

        var activeClusterNodes = responseString.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => NodeMetadata.Parse(l.Trim('\r')))
            .Where(m => !m.Flags.Failed && m.Address is { })
            .Select(meta => new ClusterNode(
                        meta.Flags.Myself
                            ? client
                            : currentState?.Nodes.FirstOrDefault(n => n.Id == meta.Id)?.Client ?? CreateClient(meta.Address!.FormatEndpoint()),
                        meta))
            .ToArray();

        foreach (var clusterNode in activeClusterNodes)
            clusterNode.AssignReplicas(activeClusterNodes);

        _state = new ClusterState(activeClusterNodes, new SlotsMap(activeClusterNodes));
    }

    private void OnRespError(RespErrorResponseException exception)
    {
        var state = _state;
        if (state is null)
            return;

        if (SlotMovedError.TryParse(exception, out var slotMovedError))
            state.Map.Update(slotMovedError);

        _ = SafeSyncClusterState();
    }

    public override string ToString()
    {
        var state = _state;
        if (state is null)
            return "Disconnected";

        var stringBuilder = new StringBuilder();
        foreach (var clusterNode in state.Nodes)
        {
            if (clusterNode.Role != Role.Primary)
                continue;

            stringBuilder.Append("+- ").Append(clusterNode.Metadata.Address?.FormatEndpoint() ?? clusterNode.Id);
            stringBuilder.Append(' ').AppendLine(clusterNode.Metadata.Slots.ToString());
            foreach (var replica in clusterNode.Replicas)
                stringBuilder.Append("|-- ").AppendLine(replica.Metadata.Address?.FormatEndpoint() ?? replica.Id);
        }

        return stringBuilder.ToString();
    }

    private RespClient CreateClient(string seedEndpoint)
    {
        static async ValueTask InitializeReadonlyModeOnReplica(ConnectedSocket socket, CancellationToken cancellationToken)
        {
            var meta = new ClusterNodeConnectionMetadata(socket.Metadata);
            if (meta.Role == Role.Primary)
                return;

            using var directSocketRpc = new DirectSocketRpc(socket.Socket);
            await directSocketRpc.Rpc("READONLY", _ => { }, cancellationToken);
        }

        return new RespClient(
            _options.ClientOptions
                .WithAdditionalConnectionInitializations(InitializeReadonlyModeOnReplica)
                .WithRespErrorObservers(OnRespError),
            _options.CreateConnectionProcedure(_options, seedEndpoint));
    }

    public RespClient PickRandomPrimary()
    {
        return RouteTo(RolePreference.Primary).PickRandomNode();
    }

    public RespClient PickRandom()
    {
        return RouteTo(RolePreference.Any).PickRandomNode();
    }

    public RoleRouter RouteTo(RolePreference preference)
    {
        switch (preference)
        {
            case RolePreference.Primary:
                return _primaryRouter;
            case RolePreference.Replica:
                return _replicaRouter;
            case RolePreference.PreferReplica:
                return _preferReplicaRouter;
            case RolePreference.Any:
                return _anyRouter;
            default:
                throw new ArgumentOutOfRangeException(nameof(preference), preference, null);
        }
    }

    public class RoleRouter(RolePreference preference, ClusterRouter outer) : IRespClient
    {
        public Command<T> CreateCommand<T>(string name, bool @unsafe = false)
            where T : IRespResponse
        {
            return Command<T>.GetCommand().Init(outer._memoryBlocks, outer._outgoingBlockSize, name, clusterMode: true, @unsafe);
        }

        public ValueTask<T> ExecuteCommand<T>(Command<T> command, CancellationToken cancellationToken = default)
            where T : IRespResponse
        {
            return ExecuteOn(command, cancellationToken);
        }

        public IEnumerable<(RespClient Node, T[] Objects)> GroupBy<T>(IEnumerable<T> objects, Func<T, Key> selectKey)
        {
            return objects.GroupBy(o => PickNode(selectKey(o))).Select(g => (Node: g.Key, Objects: g.ToArray()));
        }

        public RespClient PickRandomNode()
        {
            var clusterState = outer._state ?? throw new RespClusterNotInitializedException();
            var nodes = clusterState.Nodes;
            var node = nodes[Random.Shared.Next(0, nodes.Count)];
            return PickNodeForRole(node);
        }

        public RespClient PickNode(ushort hashSlot)
        {
            var clusterState = outer._state ?? throw new RespClusterNotInitializedException();
            var node = clusterState.Map.GetNode(hashSlot);
            return PickNodeForRole(node);
        }

        public RespClient PickNode(Key key)
        {
            var clusterState = outer._state ?? throw new RespClusterNotInitializedException();
            var node = clusterState.Map.GetNode(key.CalculateHashSlot());
            return PickNodeForRole(node);
        }

        private ValueTask<T> ExecuteOn<T>(Command<T> command, CancellationToken cancellationToken)
            where T : IRespResponse
        {
            var clusterState = outer._state ?? throw new RespClusterNotInitializedException();

            ClusterNode? destination = null;
            foreach (var slot in command.EnumerateSlots())
            {
                var node = clusterState.Map.GetNode(slot);
                if (destination is null)
                {
                    destination = node;
                    continue;
                }

                if (destination != node)
                    throw new RespClusterCommandExecutionException("Command slots point to multiple different nodes.");
            }

            if (destination is null)
                throw new RespClusterCommandExecutionException($"No slots were provided. Ensure that the command is created using {nameof(ClusterRouter)}.");

            var client = PickNodeForRole(destination);
            if (outer._options.EnableAutoRedirections)
                return ExecuteWithAutoRedirections(client, command, cancellationToken);

            return client.ExecuteCommand(command, cancellationToken);
        }

        private RespClient PickNodeForRole(ClusterNode destination)
        {
            return preference switch
            {
                RolePreference.Primary => destination.Client,
                RolePreference.PreferReplica => destination.PickReplica()?.Client ?? destination.Client,
                RolePreference.Replica => destination.PickReplica()?.Client
                                          ?? throw new RespClusterCommandExecutionException("Can't execute command on replica because none are available"),
                RolePreference.Any => destination.PickAny(),
                _ => destination.PickAny()
            };
        }

        private async ValueTask<T> ExecuteWithAutoRedirections<T>(
            RespClient client,
            Command<T> command,
            CancellationToken cancellationToken) where T : IRespResponse
        {
            var startingTimestamp = Stopwatch.GetTimestamp();
            using var commandOwnership = command.WithOwnership();

            try
            {
                return await client.ExecuteCommand(command, cancellationToken);
            }
            catch (RespErrorResponseException e)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;

                var (endpointClient, withAsking) = TryGetRedirect(e);
                if (endpointClient is null)
                    throw;

                var elapsed = Stopwatch.GetElapsedTime(startingTimestamp);
                command.WithTimeout(command.Timeout - elapsed.Milliseconds);

                if (!withAsking)
                    return await endpointClient.ExecuteCommand(command, cancellationToken);

                var combo = new Combo(endpointClient, command.Timeout);
                using var asking = endpointClient.CreateCommand<RespVoid>("ASKING");
                endpointClient.Pack(combo, asking, cancellationToken);
                var commandResponse = endpointClient.Pack(combo, command, cancellationToken);
                combo.Execute();

                return await commandResponse.Future();
            }
        }

        private (RespClient? RespClient, bool WithAsking) TryGetRedirect(RespErrorResponseException e)
        {
            var endpoint = ReadOnlySpan<char>.Empty;
            var withAsking = false;
            if (SlotMovedError.TryParse(e, out var slotMovedError))
                endpoint = slotMovedError.EndPoint;
            else if (AskRedirectionError.TryParse(e, out var askRedirectionError))
            {
                endpoint = askRedirectionError.EndPoint;
                withAsking = true;
            }

            if (endpoint.IsEmpty)
                return default;

            var state = outer._state;
            if (state is null)
                return default;

            return state.Map.TryGetNodeByEndpoint(endpoint, out var node)
                ? (node.Client, withAsking)
                : default;
        }
    }
}