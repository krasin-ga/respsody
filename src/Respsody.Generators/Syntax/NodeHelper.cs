namespace Respsody.Generators.Syntax;

internal static class NodeHelper
{
    public static IEnumerable<SyntaxNode[]> GetPossibleSequencesOfChildren(this SyntaxNode node)
    {
        if (node is not VariedSyntaxNode variedSyntaxNode)
        {
            yield return node.Children.ToArray();
            yield break;
        }

        if (node is OptionalSyntaxNode)
            yield return [];

        foreach (var variation in variedSyntaxNode.GetVariations())
            yield return variation.ToArray();
    }
}