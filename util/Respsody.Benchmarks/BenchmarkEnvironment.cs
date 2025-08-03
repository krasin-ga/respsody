using System.Net;
using Garnet;
using Garnet.server;

namespace Respsody.Benchmarks;

public static class BenchmarkEnvironment
{
    private static GarnetServer _garnet = null!;
    public static readonly IPEndPoint RespServerEndpoint = new(IPAddress.Loopback, 6399);

    public static void Cleanup()
    {
        _garnet?.Dispose();
    }

    public static void Setup()
    {
        _garnet = new GarnetServer(new GarnetServerOptions
        {
            EndPoints = [RespServerEndpoint]
        });

        _garnet.Start();
    }
}