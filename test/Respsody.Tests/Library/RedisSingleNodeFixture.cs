using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace Respsody.Tests.Library;

public class RedisSingleNodeFixture : IAsyncLifetime
{
    private const int Port = 7100;
    private const string NetworkName = "redis-single-node-net";
    private const string RedisPassword = "hi there";
    private IContainer? _container;
    private INetwork? _network;

    public string GetPassword()
        => RedisPassword;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder()
            .WithName(NetworkName)
            .Build();

        await _network.CreateAsync();

        _container = new ContainerBuilder()
            .WithImage("redis:6.2")
            .WithName($"redis-node-{Port}")
            .WithNetwork(_network.Name)
            .WithNetworkAliases($"redis-node-{Port}")
            .WithPortBinding(Port, Port)
            .WithCommand(
                "redis-server",
                "--port",
                Port.ToString(),
                "--appendonly",
                "yes",
                "--requirepass",
                RedisPassword)
            .Build();

        await _container.StartAsync();
    }

    public async Task Restart()
    {
        await _container!.StopAsync();
        await _container!.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is { })
            await _container.StopAsync();

        if (_network != null)
            await _network.DeleteAsync();
    }

    public string GetStringEndpoint()
    {
        return $"localhost:{Port}";
    }
}