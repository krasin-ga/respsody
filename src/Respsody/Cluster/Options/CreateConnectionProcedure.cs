using Respsody.Network;

namespace Respsody.Cluster.Options;

public delegate IConnectionProcedure CreateConnectionProcedure(ClusterRouterOptions options, string endpoint);