using Respsody.Library;

namespace Respsody.Cluster;

public static class RoleParser
{
    public static Role ParseRole(string role)
    {
        return ParseInternal(role) ?? throw new ArgumentOutOfRangeException(nameof(role));
    }

    private static Role? ParseInternal(string role)
    {
        if (role.EqualTo("replica", "slave"))
            return Role.Replica;

        if (role.EqualTo("primary", "master"))
            return Role.Primary;

        return null;
    }

    public static Role ParseFromFlags(string[] flags)
    {
        return flags.Select(ParseInternal).First(f => f != null)!.Value;
    }
}