namespace Respsody.Library;

internal  static class StringExtensions
{
    public static bool EqualTo(this string input, params string[] variants)
    {
        return variants.Any(variant => variant.Equals(input, StringComparison.CurrentCultureIgnoreCase));
    }
}