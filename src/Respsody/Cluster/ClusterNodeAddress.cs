using System.Net;
using System.Text.RegularExpressions;

namespace Respsody.Cluster;

public partial record ClusterNodeAddress(IPAddress IpAddress, int Port, int ClusterBusPort)
{
    public static ClusterNodeAddress? Parse(string input)
    {
        if (input == ":0@0")
            return null;

        var match = FormatRegex().Match(input);
        if (!match.Success)
            throw new FormatException($"Unable to parse the node address from the given string: {input}");

        return new ClusterNodeAddress(
            IPAddress.Parse(match.Groups["ip"].Value),
            int.Parse(match.Groups["port"].Value),
            int.Parse(match.Groups["bus_port"].Value));
    }

    [GeneratedRegex(@"^(?<ip>[^:]+):(?<port>\d+)@(?<bus_port>\d+)$")]
    private static partial Regex FormatRegex();

    public string FormatEndpoint()
    {
        return $"{IpAddress}:{Port}";
    }
}