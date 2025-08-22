using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;

namespace Respsody.Cluster.Options;

public class ClusterRouterOptions
{
    public required string[] SeedEndpoints { get; init; }
    public AuthOptions? AuthOptions { get; init; }
    public string? ClientName { get; init; } = $"{Environment.MachineName}-{Guid.NewGuid().ToString()[..8]}";
    public int ConnectionTimeoutMs { get; init; } = 10_000;
    public required RespClientOptions ClientOptions { get; init; }
    public bool EnableAutoRedirections { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; }
    public IClusterRouterHandler? ClusterRouterHandler { get; init; }
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromMinutes(5);

    public CreateConnectionProcedure CreateConnectionProcedure { get; init; } =
        (options, endpoint) => new DefaultConnectionProcedure(
            new ConnectionOptions
            {
                ConnectionTimeoutMs = options.ConnectionTimeoutMs,
                ClientName = options.ClientName,
                AuthOptions = options.AuthOptions,
                Endpoint = endpoint
            }
        );
}