using System.Net;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Network;

namespace Respsody;

public class RespClientFactory
{
    public async Task<RespClient> Create(
        RespClientOptions options,
        IConnectionProcedure connectionProcedure)
    {
        var redisClient = new RespClient(options, connectionProcedure);

        await redisClient.Connect();

        return redisClient;
    }

    public async Task<RespClient> Create(EndPoint endPoint)
    {
        var respClient = new RespClient(
            new RespClientOptions(),
            new DefaultConnectionProcedure(new ConnectionOptions { Endpoint = endPoint.ToString()! })
        );

        await respClient.Connect();

        return respClient;
    }
}