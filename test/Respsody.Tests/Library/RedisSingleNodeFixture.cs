using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using System;
using System.Collections.Generic;
using System.Threading;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Network;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Respsody.Tests.Library;

public class RedisSingleNodeFixture : IAsyncLifetime
{
    private const int Port = 6379;
    private const string RedisPassword = "hi there";
    private IContainer? _container;

    public string GetPassword()
        => RedisPassword;

    public IConnectionProcedure CreateConnectionProcedure() => new FixtureConnectionProcedure(this);

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("redis:7.4.7")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithCommand(
                "redis-server",
                "--port",
                Port.ToString(),
                "--appendonly",
                "yes",
                "--requirepass",
                RedisPassword)
            .Build();

        try
        {
            await _container.StartAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task Restart()
    {
        await _container!.StopAsync();
        await _container!.StartAsync();
    }

    public async Task DisposeAsync()
    {
        var container = _container;
        _container = null;
        if (container is not null)
            await container.DisposeAsync();
    }

    public string GetStringEndpoint()
    {
        return $"{_container!.Hostname}:{_container.GetMappedPublicPort(Port)}";
    }

    private sealed class FixtureConnectionProcedure(RedisSingleNodeFixture fixture) : IConnectionProcedure
    {
        public IReadOnlyDictionary<string, string?> Config { get; } = new Dictionary<string, string?>();
        public TimeSpan Timeout => TimeSpan.FromSeconds(10);

        public Task<ConnectedSocket> Connect(CancellationToken token) => new DefaultConnectionProcedure(new ConnectionOptions
        {
            Endpoint = fixture.GetStringEndpoint(),
            AuthOptions = new AuthOptions { Password = RedisPassword }
        }).Connect(token);
    }
}
