using System;
using System.Threading.Tasks;
using Xunit;

namespace Respsody.Tests;

public class ClusterRandomRoutingTests
{
    [Fact]
    public async Task RandomRoutingRespectsRequestedRoleWhenTopologyContainsReplicas()
    {
        await using var server = new ClusterTestServer();
        server.Respond = (_, _) => Task.FromResult(ClusterTestServer.Bulk(server.Topology +
            "node1 127.0.0.1:7001@17001 slave node0 0 0 1 connected\n" +
            "node2 127.0.0.1:7002@17002 slave node0 0 0 1 connected\n"));
        using var router = ClusterRefreshTests.CreateRouter(server);
        await router.Initialize().WaitAsync(TimeSpan.FromSeconds(10));
        var primary = router.RouteTo(RolePreference.Primary).PickNode((ushort)0);

        for (var index = 0; index < 128; index++)
        {
            Assert.Same(primary, router.PickRandomPrimary());
            Assert.NotSame(primary, router.RouteTo(RolePreference.Replica).PickRandomNode());
            Assert.NotSame(primary, router.RouteTo(RolePreference.PreferReplica).PickRandomNode());
        }
    }
}
