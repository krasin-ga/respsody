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
    public IReadOnlyDictionary<string, object>? Props { get; init; } = null;
    public required RespClientOptions ClientOptions { get; init; }
    public bool EnableAutoRedirections { get; init; }

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