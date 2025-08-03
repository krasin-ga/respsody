namespace Respsody.Generators.Syntax;

internal class RawNode(string value)
{
    public string Value { get; set; } = value;
    public List<RawNode> Children { get; } = [];

    public void AddChild(RawNode node)
    {
        Children.Add(node);
    }

    public IEnumerable<IReadOnlyList<RawNode>> SplitByValue(string value)
    {
        var group = new List<RawNode>();

        foreach (var rawNode in Children)
        {
            if (rawNode.Value == value)
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

    public bool Is(string value)
    {
        return Value == value;
    }
}