using Microsoft.CodeAnalysis.CSharp;

namespace Respsody.Generators.Syntax;

public static class IdentifierExtensions
{
    public static string ToValidIdentifier(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "_";

        var sb = new StringBuilder();
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }

        if (!char.IsLetter(sb[0]) && sb[0] != '_')
            sb.Insert(0, '_');

        var identifier = sb.ToString();

        if (SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None)
            identifier = "@" + identifier;

        return identifier;
    }
}