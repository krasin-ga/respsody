namespace Respsody.Cluster;

public class ClusterNodeFlags
{
    public bool Myself { get; }
    public bool Primary { get; }
    public bool Replica { get; }
    public bool MaybeFailed { get; }
    public bool Failed { get; }
    public bool NoAddress { get; }
    public bool NoFailover { get; }
    public bool NoFlags { get; }

    public ClusterNodeFlags(string[] flags)
    {
        foreach (var flag in flags)
        {
            switch (flag)
            {
                case "myself":
                    Myself = true;
                    break;
                case "master":
                    Primary = true;
                    break;
                case "slave":
                    Replica = true;
                    break;
                case "fail?":
                    MaybeFailed = true;
                    break;
                case "fail":
                    Failed = true;
                    break;
                case "noaddr":
                    NoAddress = true;
                    break;
                case "nofailover":
                    NoFailover = true;
                    break;
                case "noflags":
                    NoFlags = true;
                    break;
            }
        }
    }
}