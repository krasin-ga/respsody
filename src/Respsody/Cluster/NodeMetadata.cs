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
        var split = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length < 8)
            throw new FormatException("A CLUSTER NODES entry must contain at least eight fields.");
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
