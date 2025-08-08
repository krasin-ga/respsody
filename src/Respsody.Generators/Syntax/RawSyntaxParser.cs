using System.Text.RegularExpressions;

namespace Respsody.Generators.Syntax;

internal class RawSyntaxParser
{
    public RawNode Parse(string input)
    {
        var tokens = Tokenize(input);

        var index = 0;
        return ParseNode(tokens, NodeType.Command, expectedEnd: null, ref index);
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        var appendingConstant = false;
        foreach (var c in input)
        {
            if (c == '`' || appendingConstant)
            {
                token.Append(c);

                if (appendingConstant && c == '`')
                {
                    PushCurrentToken(token, tokens);
                    appendingConstant = false;
                    continue;
                }

                appendingConstant = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                PushCurrentToken(token, tokens);
            }
            else if (c.IsBracket())
            {
                PushCurrentToken(token, tokens);
                tokens.Add(c.ToString());
            }
            else if (c == '.')
            {
                var allDots = true;
                for (var i = 0; i < token.Length; i++)
                {
                    if (token[i] == '.')
                        continue;

                    allDots = false;
                    break;
                }

                if (!allDots)
                    PushCurrentToken(token, tokens);

                token.Append(c);
            }
            else
            {
                token.Append(c);
            }
        }

        if (token.Length > 0)
            tokens.Add(token.ToString());

        return tokens;
    }

    private static void PushCurrentToken(StringBuilder token, List<string> tokens)
    {
        if (token.Length <= 0)
            return;
        tokens.Add(token.ToString());
        token.Clear();
    }

    private static RawNode ParseNode(
        IReadOnlyList<string> tokens,
        string tokenValue,
        string? expectedEnd,
        ref int index)
    {
        var root = new RawNode(tokenValue);

        while (index < tokens.Count)
        {
            var token = tokens[index++];

            if (token is "[")
            {
                var child = ParseNode(tokens, NodeType.Optional, expectedEnd: "]", ref index);
                root.AddChild(child);

                continue;
            }

            if (token is "<")
            {
                var child = ParseNode(tokens, NodeType.OneOff, expectedEnd: ">", ref index);
                root.AddChild(child);

                continue;
            }

            if (token.Contains("`"))
            {
                if (expectedEnd != null)
                    throw new FormatException("Literals are not allowed inside blocks");

                root.AddChild(new RawNode(token));

                continue;
            }

            if (token.IsClosingBracket())
            {
                if (token == expectedEnd)
                    return root;

                throw new FormatException("Invalid sequence of brackets");
            }

            root.AddChild(new RawNode(token));
        }

        if (expectedEnd != null)
            throw new FormatException($"Expected: {expectedEnd}");
        return root;
    }
}