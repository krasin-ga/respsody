namespace Respsody.Generators.Syntax;

internal static class TokenExtensions
{
    public static bool IsClosingBracket(this string token)
    {
        return token is "]" or ">";
    }

    public static bool IsBracket(this char @char)
    {
        return @char is '[' or ']' or '<' or '>';
    }
}