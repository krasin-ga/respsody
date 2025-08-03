namespace Respsody.Generators.Syntax;

internal class OrSyntaxNode : SyntaxNode
{
    public override ICommandMethodBuilder MethodBuilder => throw new NotSupportedException();

    public override void AddChild(SyntaxNode node)
    {
        throw new NotSupportedException();
    }

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is OrSyntaxNode;

    protected override int CalculateHashCode()
        => GetType().GetHashCode();

    public override string ToDisplayString()
        => "|";
}