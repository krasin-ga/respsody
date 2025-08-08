namespace Respsody.Generators.Syntax;

internal class RawNode(string value)
{
    public string Value { get; set; } = value;
    public List<RawNode> Children { get; } = [];

    public void AddChild(RawNode node)
    {
        Children.Add(node);
    }

    public bool Is(string token)
    {
        return Value == token;
    }

    public bool IsConstantLiteral()
    {
        return Value.StartsWith("`");
    }
}