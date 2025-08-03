using System.Diagnostics.Contracts;
using System.Net;
using Respsody.Exceptions;

namespace Respsody.Client.Connection.Options;

public class ConnectionOptions
{
    public string Endpoint { get; init; } = "localhost:6379";
    public AuthOptions? AuthOptions { get; init; }
    public string? ClientName { get; init; } = $"{Environment.MachineName}-{Guid.NewGuid().ToString()[..8]}";
    public int ConnectionTimeoutMs { get; init; } = 10_000;

    public EndPoint GetEndPoint()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
            throw new RespConnectionException("Endpoint is not set");

        if (IPEndPoint.TryParse(Endpoint, out var ipEndPoint))
            return ipEndPoint;

        var splitted = Endpoint.Split(':');
        if (splitted.Length > 2)
            ThrowIncorrectEndpointFormatException();

        var port = 6379;
        if (splitted.Length != 2)
            return new DnsEndPoint(splitted[0], port);

        if (!int.TryParse(splitted[1], out port))
            ThrowIncorrectEndpointFormatException();

        return new DnsEndPoint(splitted[0], port);
    }

    [Pure]
    public ConnectionOptions WithEndpoint(string endpoint)
    {
        return new ConnectionOptions
        {
            ConnectionTimeoutMs = ConnectionTimeoutMs,
            AuthOptions = AuthOptions,
            ClientName = ClientName,
            Endpoint = endpoint
        };
    }

    private void ThrowIncorrectEndpointFormatException()
    {
        throw new RespConnectionException($"Endpoint format is incorrect: {Endpoint}");
    }
}