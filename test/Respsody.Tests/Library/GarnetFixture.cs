using System;
using System.Net;
using System.Threading;
using Garnet;
using Garnet.server;

namespace Respsody.Tests.Library;

public sealed class GarnetFixture : IDisposable
{
    private static int _freePort = 7891;

    private readonly GarnetInstance _instance = CreateGarnet();

    public static GarnetInstance CreateGarnet()
    {
        var failedAttempts = 0;
        while (failedAttempts < 100)
        {
            GarnetServer? garnet = null;
            try
            {
                var port = Interlocked.Increment(ref _freePort);
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                garnet = new GarnetServer(new GarnetServerOptions
                {
                    EndPoints = [endpoint],
                    PageSize = "256m"
                });

                garnet.Start();

                return new GarnetInstance(garnet, endpoint);
            }
            catch
            {
                garnet?.Dispose();
                failedAttempts++;
            }
        }

        throw new InvalidOperationException("Failed to create Garnet");
    }

    public EndPoint GetEndpoint()
    {
        return _instance.EndPoint;
    }

    public string GetStringEndpoint()
    {
        return _instance.EndPoint.ToString()!;
    }

    public void Dispose()
    {
        _instance.Dispose();
    }

    public sealed record GarnetInstance(GarnetServer Garnet, IPEndPoint EndPoint) : IDisposable
    {
        public void Dispose()
        {
            Garnet.Dispose();
        }
    }
}