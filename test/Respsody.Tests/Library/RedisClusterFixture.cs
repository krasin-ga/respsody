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
    private const string RedisPassword = "hi there";
    private readonly List<(IContainer Container, int Port)> _redisNodes = new();
    private INetwork? _network;

    public string GetRandomEndpoint()
    {
        var (container, port) = _redisNodes[Random.Shared.Next(_redisNodes.Count)];
        return $"{container.Hostname}:{container.GetMappedPublicPort(port)}";
    }

    public string GetHostEndpoint(string endpoint)
    {
        var port = int.Parse(endpoint.AsSpan(endpoint.LastIndexOf(':') + 1));
        foreach (var (container, internalPort) in _redisNodes)
            if (internalPort == port)
                return $"{container.Hostname}:{container.GetMappedPublicPort(internalPort)}";
        return endpoint;
    }

    public string GetPassword()
        => RedisPassword;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder()
            .Build();

        await _network.CreateAsync();

        for (var i = 0; i < NodeCount; i++)
        {
            var port = StartPort + i;
            var container = new ContainerBuilder("redis:6.2")
                .WithNetwork(_network.Name)
                .WithNetworkAliases($"redis-node-{port}")
                .WithPortBinding(port, assignRandomHostPort: true)
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
