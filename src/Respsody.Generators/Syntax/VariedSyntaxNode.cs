namespace Respsody.Generators.Syntax;

internal abstract class VariedSyntaxNode : SyntaxNode
{
    public virtual IEnumerable<IEnumerable<SyntaxNode>> GetVariations()
    {
        var group = new List<SyntaxNode>();

        foreach (var rawNode in Children)
        {
            if (rawNode is OrSyntaxNode)
            {
                yield return group;
                group = [];
                continue;
            }

            group.Add(rawNode);
        }

        if (group.Count > 0)
            yield return group;
    }

    protected sealed override int CalculateHashCode()
    {
        return Children.Select(it => it.GetHashCode()).Aggregate(97, (a, b) => ((a << 7) + a) ^ b)
               + GetType().GetHashCode();
    }
}