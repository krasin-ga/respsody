namespace Respsody.Generators.Syntax;

internal class CommandSyntaxParser
{
    public static CommandSyntaxNode Parse(string commandSyntax)
    {
        var rawParser = new RawSyntaxParser();

        var rootNode = rawParser.Parse(commandSyntax);

        if (!rootNode.Children.Any())
            throw new Exception();

        var commandNode = new CommandSyntaxNode(
            rootNode.Children[0].Value);

        foreach (var child in rootNode.Children.Skip(1))
            commandNode.AddChild(Translate(child));

        return commandNode;
    }

    private static SyntaxNode Translate(RawNode node)
    {
        if (node.Is(NodeType.Or))
            return new OrSyntaxNode();

        var token = node.Value;

        if (node.Is(NodeType.Key))
            return new KeySyntaxNode();

        if (OptionSyntaxNode.MatchFormat(token))
            return new OptionSyntaxNode(token);

        if (char.IsLower(token[0]) && token.All(c => c is ':' or '-' || char.IsLetter(c)))
            return ParameterSyntaxNode.FromRawToken(token);

        if (token.All(c => c is '.'))
            return new ParamsSpecifierSyntaxNode();

        SyntaxNode aggregateNode = node.Value switch
        {
            NodeType.Optional => new OptionalSyntaxNode(),
            NodeType.OneOff => new OneOfSyntaxNode(),
            _ => throw new ArgumentOutOfRangeException()
        };

        for (var i = 0; i < node.Children.Count; i++)
        {
            var nodeChild = node.Children[i];
            var syntaxNode = Translate(nodeChild);

            if (syntaxNode is not ParamsSpecifierSyntaxNode)
            {
                aggregateNode.AddChild(syntaxNode);
                continue;
            }

            if (i != node.Children.Count - 1)
                throw new FormatException("Expected range specifier at the end of []");

            var existingChildren = aggregateNode.Children;
            aggregateNode = new ArraySyntaxNode();
            foreach (var child in existingChildren)
                aggregateNode.AddChild(child);
        }

        return aggregateNode;
    }
}