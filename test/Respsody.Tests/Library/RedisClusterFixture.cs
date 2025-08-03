using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace Respsody.Tests.Library;

public class RedisClusterFixture : IAsyncLifetime
{
    private const int StartPort = 7000;
    private const int NodeCount = 9;
    private const string NetworkName = "redis-cluster-net";
    private const string RedisPassword = "hi there";
    private readonly List<(IContainer Container, int Port)> _redisNodes = new();
    private INetwork? _network;

    public string GetRandomEndpoint()
    {
        return $"localhost:{StartPort + Random.Shared.Next(0, NodeCount)}";
    }

    public string GetPassword()
        => RedisPassword;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder()
            .WithName(NetworkName)
            .Build();

        await _network.CreateAsync();

        for (var i = 0; i < NodeCount; i++)
        {
            var port = StartPort + i;
            var container = new ContainerBuilder()
                .WithImage("redis:6.2")
                .WithName($"redis-node-{port}")
                .WithNetwork(_network.Name)
                .WithNetworkAliases($"redis-node-{port}")
                .WithPortBinding(port, port)
                .WithCommand(
                    "redis-server",
                    "--port",
                    port.ToString(),
                    "--cluster-enabled",
                    "yes",
                    "--cluster-config-file",
                    "/data/nodes.conf",
                    "--cluster-node-timeout",
                    "5000",
                    "--appendonly",
                    "yes",
                    "--requirepass",
                    RedisPassword,
                    "--masterauth",
                    RedisPassword)
                .Build();

            _redisNodes.Add((container, port));
            await container.StartAsync();
        }

        var clusterNodes = new List<string>();
        foreach (var (container, port) in _redisNodes)
        {
            var ipAddress = container.IpAddress;
            if (string.IsNullOrEmpty(ipAddress))
                throw new Exception($"Failed to retrieve IP address for container {container.Name}");
            clusterNodes.Add($"{ipAddress}:{port}");
        }

        var clusterCmd = new List<string>
        {
            "redis-cli", "-a", RedisPassword, "--cluster", "create"
        };
        clusterCmd.AddRange(clusterNodes);
        clusterCmd.Add("--cluster-replicas");
        clusterCmd.Add("2");
        clusterCmd.Add("--cluster-yes");

        var execResult = await _redisNodes[0].Container.ExecAsync(clusterCmd);

        if (execResult.ExitCode != 0)
            throw new Exception($"Cluster creation failed: {execResult.Stderr}");

        await Task.Delay(5000);
    }

    public async Task DisposeAsync()
    {
        foreach (var (container, _) in _redisNodes)
            await container.StopAsync();

        if (_network != null)
            await _network.DeleteAsync();
    }
}