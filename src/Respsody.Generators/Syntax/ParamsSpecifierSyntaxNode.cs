namespace Respsody.Generators.Syntax;

internal class ParamsSpecifierSyntaxNode : SyntaxNode
{
    public override ICommandMethodBuilder MethodBuilder
        => throw new NotSupportedException();

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is ParamsSpecifierSyntaxNode;

    protected override int CalculateHashCode()
        => 1;

    public override string ToDisplayString()
        => "...";
}