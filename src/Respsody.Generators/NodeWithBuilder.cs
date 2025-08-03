using Respsody.Generators.Syntax;

namespace Respsody.Generators;

internal class NodeWithBuilder(Syntax.SyntaxNode node, ICommandMethodBuilder builder)
{
    public Syntax.SyntaxNode Node { get; } = node;
    public ICommandMethodBuilder Builder { get; } = builder;
}