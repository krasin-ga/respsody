using Respsody.Library;
using Respsody.Network;

namespace Respsody.Cluster;

public class ClusterNodeConnectionMetadata(ConnectionMetadata connectionMetadata)
{
    public Role Role { get; } = connectionMetadata.CastOrThrow("role", v => RoleParser.ParseRole(v.Cast<string>()));
    public ServerMode ServerMode { get; } = connectionMetadata.CastOrThrow("mode", v => new ServerMode(v.Cast<string>()));
}