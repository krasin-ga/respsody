namespace Respsody.Generators.Syntax;

internal class OptionalSyntaxNode : VariedSyntaxNode
{
    public override ICommandMethodBuilder MethodBuilder => throw new NotSupportedException();

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is OptionalSyntaxNode && Children.SequenceEqual(node.Children);

    public override string ToDisplayString()
        => $"[{string.Join(" ",Children.Select(c => c.ToDisplayString()))}]";
}