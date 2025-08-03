using System.Diagnostics;

namespace Respsody.Generators.Syntax;

[DebuggerDisplay("{ToDisplayString()}")]
internal abstract class SyntaxNode
{
    public List<SyntaxNode> Children { get; } = [];

    public abstract ICommandMethodBuilder MethodBuilder { get; }

    public virtual void AddChild(SyntaxNode node)
    {
        Children.Add(node);
    }

    public sealed override bool Equals(object? obj)
    {
        return obj is SyntaxNode syntaxNode && IsEquivalentTo(syntaxNode);
    }

    public sealed override int GetHashCode()
        => CalculateHashCode();

    protected abstract bool IsEquivalentTo(SyntaxNode node);
    protected abstract int CalculateHashCode();
    public abstract string ToDisplayString();

    protected int CalculateHashCodeForChildren()
    {
        return Children.Select(it => it.GetHashCode()).Aggregate(97, (a, b) => ((a << 7) + a) ^ b)
               + this.GetType().GetHashCode();
    }
}