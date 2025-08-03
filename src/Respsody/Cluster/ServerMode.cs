namespace Respsody.Cluster;

public class ServerMode(string mode)
{
    public string Value { get; } = mode;

    public bool IsStandalone { get; } = ModeIs(mode, "standalone");
    public bool IsCluster { get; } = ModeIs(mode, "cluster");
    public bool IsSentinel { get; } = ModeIs(mode, "sentinel");

    private static bool ModeIs(string mode, string value)
    {
        return mode.Equals(value, StringComparison.OrdinalIgnoreCase);
    }
}