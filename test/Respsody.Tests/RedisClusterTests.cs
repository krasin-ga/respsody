using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Cluster;
using Respsody.Cluster.Options;
using Respsody.Tests.Library;
using Xunit;
using Xunit.Abstractions;
using static Respsody.Cluster.RolePreference;
using static Respsody.Tests.CLUSTER;

namespace Respsody.Tests;

public class RedisClusterTests(RedisClusterFixture fixture, ITestOutputHelper @out) : IClassFixture<RedisClusterFixture>
{
    private const int NumberOfKeysToTest = 100_000;

    [Fact]
    public async Task TestClusterFunctionality()
    {
        using var clusterRouter = new ClusterRouter(
            new ClusterRouterOptions
            {
                ClusterRouterHandler = new TestClusterRouterHandler(@out),
                EnableAutoRedirections = true,
                SeedEndpoints = [fixture.GetRandomEndpoint()],
                ClientOptions = new RespClientOptions
                {
                    IncomingMemoryBlockSize = 120,
                    OutgoingMemoryBlockSize = 120
                },
                AuthOptions = new AuthOptions
                {
                    Password = fixture.GetPassword()
                },
                ConnectionTimeoutMs = 10_000,
                CreateConnectionProcedure =
                    (options, endpoint) => new DefaultConnectionProcedure(
                        new ConnectionOptions
                        {
                            ConnectionTimeoutMs = options.ConnectionTimeoutMs,
                            ClientName = options.ClientName,
                            AuthOptions = options.AuthOptions,
                            Endpoint = $"localhost:{endpoint.Split(":")[1]}"
                        }
                    )
            });

        await clusterRouter.Initialize();

        using var info = await clusterRouter.PickRandomPrimary().Cluster(INFO);
        var infoString = info.ToString(Encoding.UTF8);

        @out.WriteLine(infoString);

        Assert.Contains("cluster_state:ok", infoString);

        @out.WriteLine(clusterRouter.ToString());

        var primaryRouter = clusterRouter.RouteTo(Primary);
        var cts = new CancellationTokenSource();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cts.Token,
            MaxDegreeOfParallelism = Environment.ProcessorCount * 32
        };

        for (var i = 0; i < 5; i++)
        {
            await WriteKeysToPrimaryNodes(parallelOptions, primaryRouter);
            @out.WriteLine("done writing keys");

            await ReadKeysFromReplicas(parallelOptions, clusterRouter);
            @out.WriteLine("done reading keys");
        }
    }

    private async Task WriteKeysToPrimaryNodes(ParallelOptions parallelOptions, ClusterRouter.RoleRouter primaryRouter)
    {
        await Parallel.ForEachAsync(
            Enumerable.Range(0, NumberOfKeysToTest),
            parallelOptions,
            async (i, cancellation) =>
            {
                var key = CreateKey(i);
                var value = CreateValue(i);

                try
                {
                    await primaryRouter.Set(key, Value.Unicode(value), cancellation);

                    using var respString = await primaryRouter.Get(key, cancellation);

                    var sequenceEqual = respString.AsUnicodeSpan().SequenceEqual(value);
                    if (!sequenceEqual)
                        @out.WriteLine($"expected: {value} | actual: {respString.AsUnicodeSpan().ToString()}");

                    Assert.True(sequenceEqual);
                }
                catch (Exception e)
                {
                    @out.WriteLine(e.ToString());
                    throw;
                }
            });
    }

    private async Task ReadKeysFromReplicas(ParallelOptions parallelOptions, ClusterRouter clusterRouter)
    {
        await Parallel.ForEachAsync(
            Enumerable.Range(0, NumberOfKeysToTest),
            parallelOptions,
            async (i, cancellation) =>
            {
                var key = CreateKey(i);
                var value = CreateValue(i);
                var replica = clusterRouter.RouteTo(Replica);

                try
                {
                    using var respString = await replica.Get(key, cancellation);

                    var sequenceEqual = respString.AsUnicodeSpan().SequenceEqual(value);
                    if (!sequenceEqual)
                        @out.WriteLine($"expected: {value} | actual: {respString.AsUnicodeSpan().ToString()}");

                    Assert.True(sequenceEqual);
                }
                catch (Exception e)
                {
                    @out.WriteLine(
                        $"""
                         slot = {key.CalculateHashSlot()}

                         {e}

                         ----
                         """);
                    throw;
                }
            });
    }

    private static Key CreateKey(int i)
    {
        return Key.Unicode(i.ToString());
    }

    private static string CreateValue(int i)
    {
        return new string((char)(i % 256), (i + 1) % 512);
    }
}