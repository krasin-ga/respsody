using System.Text.RegularExpressions;

namespace Respsody.Generators.Library;

internal static class NamingExtensions
{
    private static readonly ThreadLocal<Regex> SafeNameRegex = new(
        () => new Regex(@"[:<>`,().*?\[\]]|\s", RegexOptions.Compiled)
    );

    public static string GetSafeName(this string name)
    {
        return SafeNameRegex.Value.Replace(name, "_");
    }
}