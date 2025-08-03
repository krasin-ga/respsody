using Respsody.Library;

namespace Respsody.Cluster;

public class LinkState(string linkState)
{
    public string Value { get; } = linkState;
    public bool IsConnected { get; } = linkState.EqualTo("connected");
}