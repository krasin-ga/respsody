namespace Respsody.Cluster;

public record NodeMetadata(
    ClusterNodeAddress? Address,
    string? Hostname,
    string Id,
    Role Role,
    Slots Slots,
    ClusterNodeFlags Flags,
    LinkState LinkState,
    string? PrimaryId,
    long PingSent,
    long PongReceived,
    long ConfigEpoch)
{
    public static NodeMetadata Parse(string line)
    {
        // <id> <ip:port@cport[,hostname]> <flags> <master> <ping-sent> <pong-recv> <config-epoch> <link-state> <slot> <slot> ... <slot>

        var split = line.Split(' ');
        var id = split[0];
        var address = split[1];
        var addressSplit = address.Split(',');

        var hostName = addressSplit.Length == 2
            ? addressSplit[1]
            : null;

        var flags = split[2].Split(',');
        var primary = split[3];
        var pingSent = split[4];
        var pongReceived = split[5];
        var configEpoch = split[6];
        var linkState = split[7];
        var slots = split[8..].Select(SlotRange.Parse).ToArray();

        return new NodeMetadata(
            Address: ClusterNodeAddress.Parse(addressSplit[0]),
            Hostname: hostName,
            Id: id,
            Role: RoleParser.ParseFromFlags(flags),
            Slots: new Slots(slots),
            Flags: new ClusterNodeFlags(flags),
            LinkState: new LinkState(linkState),
            PrimaryId: primary,
            PingSent: long.Parse(pingSent),
            PongReceived: long.Parse(pongReceived),
            ConfigEpoch: long.Parse(configEpoch));
    }
}