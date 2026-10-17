using System.Diagnostics;
using System.Text;
using System.Runtime.ExceptionServices;
using Respsody.Client;
using Respsody.Cluster.Errors;
using Respsody.Cluster.Options;
using Respsody.Exceptions;
using Respsody.Library.Disposables;
using Respsody.Memory;
using Respsody.Network;
using Respsody.Resp;

namespace Respsody.Cluster;

public sealed class ClusterRouter : IDisposable
{
    private readonly RoleRouter _anyRouter;
    private readonly CancellationTokenSource _cancellation;
    private readonly object _lifecycleLock = new();
    private readonly MemoryBlocks _memoryBlocks;
    private readonly ClusterRouterOptions _options;
    private readonly int _outgoingBlockSize;
    private readonly RoleRouter _preferReplicaRouter;
    private readonly RoleRouter _primaryRouter;
    private readonly RoleRouter _replicaRouter;
    private readonly TimeSpan _syncInterval;
    private bool _isInitialized;
    private int _isSyncing;
    private volatile ClusterState? _state;
    private int _syncTimerStarted;
    private volatile bool _isDisposed;

    public IReadOnlyDictionary<string, object> Metadata => _options.Metadata;

    public ClusterRouter(ClusterRouterOptions options)
    {
        _options = options;
        _memoryBlocks = options.ClientOptions.MemoryBlocks;
        _anyRouter = new RoleRouter(RolePreference.Any, this);
        _primaryRouter = new RoleRouter(RolePreference.Primary, this);
        _preferReplicaRouter = new RoleRouter(RolePreference.PreferReplica, this);
        _replicaRouter = new RoleRouter(RolePreference.Replica, this);
        _outgoingBlockSize = options.ClientOptions.OutgoingMemoryBlockSize;
        _syncInterval = options.SyncInterval;
        _cancellation = new CancellationTokenSource();
    }

    public async Task Initialize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (Volatile.Read(ref _isInitialized))
            return;

        if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
            throw new RespClusterInitializationAlreadyInProgressException();

        using Defer defer = new(() => _isSyncing = 0);

        if (_options.SeedEndpoints.Length == 0)
            throw new RespClusterInitializationFailedException(
                [new InvalidOperationException("SeedEndpoints.Length == 0")]);

        var (isSucceeded, exceptions) = await SyncClusterStateInternal(_options.SeedEndpoints);
        if (!isSucceeded)
            throw new RespClusterInitializationFailedException(exceptions);

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _isInitialized = true;
            if (Interlocked.CompareExchange(ref _syncTimerStarted, 1, 0) != 0)
                return;

            _ = Task.Run(SyncPeriodically);
        }
    }

    private async Task SyncPeriodically()
    {
        using var repeatableTimer = new PeriodicTimer(_syncInterval);
        try
        {
            while (await repeatableTimer.WaitForNextTickAsync(_cancellation.Token))
                await SyncClusterState();
        }
        catch (OperationCanceledException) when (_isDisposed) { }
        catch (ObjectDisposedException) when (_isDisposed) { }
    }

    public async Task<(bool IsSucceeded, IReadOnlyList<Exception> Exceptions)> SyncClusterState()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
            return (false, []);

        using Defer _ = new(() => _isSyncing = 0);

        var state = _state;
        if (state is null)
            throw new RespClusterNotInitializedException();

        var endpoints = state.Nodes.Select(n => n.Metadata.Address?.FormatEndpoint())
            .Concat(_options.SeedEndpoints).Where(e => !string.IsNullOrWhiteSpace(e));

        return await SyncClusterStateInternal(endpoints!);
    }

    private async Task<(bool IsSucceeded, IReadOnlyList<Exception> Exceptions)> SyncClusterStateInternal(IEnumerable<string> endpoints)
    {
        List<Exception>? exceptions = null;
        var updated = false;
        foreach (var seedEndpoint in endpoints.OrderBy(static _ => Random.Shared.Next()))
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            try
            {
                await UpdateClusterStateFromSeed(seedEndpoint);
                updated = true;
                break;
            }
            catch (Exception) when (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ClusterRouter));
            }
            catch (Exception exception)
            {
                _options.ClusterRouterHandler?.OnFailedToRefreshClusterSateFromSeed(this, seedEndpoint, exception);
                (exceptions ??= []).Add(exception);
            }
        }

        if (updated && _state is { Nodes.Count: > 0 } state)
        {
            _options.ClusterRouterHandler?.OnClusterStateSynced(this, state);

            return (true, exceptions ?? []);
        }

        _options.ClusterRouterHandler?.OnFailedToSyncClusterState(this, exceptions ?? []);

        return (false, exceptions ?? []);
    }

    private async Task UpdateClusterStateFromSeed(string seedEndpoint)
    {
        var currentState = _state;

        var client = CreateClient(seedEndpoint);
        List<RespClient> createdClients = [client];
        ClusterNode[]? activeClusterNodes = null;
        var published = false;
        try
        {
            Task start;
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                start = client.Start();
            }
            await start.WaitAsync(_cancellation.Token);

            var connectionMetadata = new ClusterNodeConnectionMetadata(client.Metadata!);
            if (!connectionMetadata.ServerMode.IsCluster)
                throw new RespConnectionException("Seed node's mode must be `cluster`");

            using var nodesCommand = client.CreateCommand<RespString>("CLUSTER").Arg("NODES");
            using var response = await client.ExecuteCommand(nodesCommand, _cancellation.Token);
            var responseString = response.ToString() ?? throw new RespUnexpectedOperationException(
                "Failed to get cluster nodes because response is null");

            activeClusterNodes = responseString.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => NodeMetadata.Parse(l.Trim('\r')))
                .Where(m => !m.Flags.Failed && m.Address is { })
                .Select(meta =>
                {
                    var nodeClient = currentState?.Nodes.FirstOrDefault(n => n.Id == meta.Id && n.Metadata.Address == meta.Address)?.Client;
                    if (nodeClient is null)
                    {
                        nodeClient = meta.Flags.Myself ? client : CreateClient(meta.Address!.FormatEndpoint());
                        if (!ReferenceEquals(nodeClient, client))
                            createdClients.Add(nodeClient);
                    }

                    return new ClusterNode(nodeClient, meta);
                })
                .ToArray();

            if (activeClusterNodes.Length == 0)
                throw new RespConnectionException("Cluster topology contains no active nodes.");

            foreach (var clusterNode in activeClusterNodes)
                clusterNode.AssignReplicas(activeClusterNodes);

            var nextState = new ClusterState(activeClusterNodes, new SlotsMap(activeClusterNodes));
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _state = nextState;
                published = true;
                _ = Task.WhenAll(nextState.Nodes.Select(n => n.Client.Start()));
            }

            foreach (var node in currentState?.Nodes ?? [])
                if (!activeClusterNodes.Any(n => ReferenceEquals(n.Client, node.Client)))
                    node.Client.Dispose();
        }
        finally
        {
            foreach (var createdClient in createdClients)
                if (!published || !activeClusterNodes!.Any(n => ReferenceEquals(n.Client, createdClient)))
                    createdClient.Dispose();
        }
    }

    private void OnRespError(RespErrorResponseException exception)
    {
        if (_isDisposed)
            return;

        var state = _state;
        if (state is null)
            return;

        if (SlotMovedError.TryParse(exception, out var slotMovedError))
            state.Map.Update(slotMovedError);

        _ = SyncClusterState();
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

    public void Dispose()
    {
        ClusterState? state;
        lock (_lifecycleLock)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            state = _state;
            _state = null;
        }
        _cancellation.Cancel();

        foreach (var node in state?.Nodes ?? [])
            node.Client.Dispose();
    }

    private RespClient CreateClient(string seedEndpoint)
    {
        static async ValueTask InitializeReadonlyModeOnReplica(ConnectedSocket socket, CancellationToken cancellationToken)
        {
            var meta = new ClusterNodeConnectionMetadata(socket.Metadata);
            if (meta.Role == Role.Primary)
                return;

            using var directSocketRpc = new DirectSocketRpc(socket.Socket);
            using var ro = await directSocketRpc.Rpc("READONLY", _ => { }, cancellationToken);
        }

        return new RespClient(
            _options.ClientOptions
                .WithAdditionalConnectionInitializations(InitializeReadonlyModeOnReplica)
                .WithRespErrorObservers(OnRespError),
            _options.CreateConnectionProcedure(_options, seedEndpoint));
    }

    public RespClient PickRandomPrimary()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return RouteTo(RolePreference.Primary).PickRandomNode();
    }

    public RespClient PickRandom()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return RouteTo(RolePreference.Any).PickRandomNode();
    }

    public RoleRouter RouteTo(RolePreference preference)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

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
            ObjectDisposedException.ThrowIf(outer._isDisposed, outer);
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
            ObjectDisposedException.ThrowIf(outer._isDisposed, outer);
            var clusterState = outer._state ?? throw new RespClusterNotInitializedException();
            var nodes = clusterState.Nodes;
            var node = nodes[Random.Shared.Next(0, nodes.Count)];
            return PickNodeForRole(node);
        }

        public RespClient PickNode(ushort hashSlot)
        {
            ObjectDisposedException.ThrowIf(outer._isDisposed, outer);
            var clusterState = outer._state ?? throw new RespClusterNotInitializedException();
            var node = clusterState.Map.GetNode(hashSlot);
            return PickNodeForRole(node);
        }

        public RespClient PickNode(Key key)
        {
            ObjectDisposedException.ThrowIf(outer._isDisposed, outer);
            var clusterState = outer._state ?? throw new RespClusterNotInitializedException();
            var node = clusterState.Map.GetNode(key.CalculateHashSlot());
            return PickNodeForRole(node);
        }

        private ValueTask<T> ExecuteOn<T>(Command<T> command, CancellationToken cancellationToken)
            where T : IRespResponse
        {
            ObjectDisposedException.ThrowIf(outer._isDisposed, outer);
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

                var elapsedMilliseconds = (long)Math.Ceiling(Stopwatch.GetElapsedTime(startingTimestamp).TotalMilliseconds);
                var remainingTimeout = command.Timeout - elapsedMilliseconds;
                if (remainingTimeout <= 0)
                    throw new TimeoutException("The command timeout elapsed before redirection.");
                command.WithTimeout((int)remainingTimeout);

                if (!withAsking)
                    return await endpointClient.ExecuteCommand(command, cancellationToken);

                using var combo = new Combo(endpointClient, command.Timeout);
                using var asking = endpointClient.CreateCommand<RespVoid>("ASKING").WithTimeout(command.Timeout);
                var askingResponse = endpointClient.Pack(combo, asking, cancellationToken);
                var commandResponse = endpointClient.Pack(combo, command, cancellationToken);
                combo.Execute();

                Exception? askingError = null;
                try
                {
                    using var acknowledgement = await askingResponse.Future();
                }
                catch (Exception exception)
                {
                    askingError = exception;
                }

                var result = await commandResponse.Future();
                if (askingError is not null)
                {
                    result.Dispose();
                    ExceptionDispatchInfo.Throw(askingError);
                }

                return result;
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
