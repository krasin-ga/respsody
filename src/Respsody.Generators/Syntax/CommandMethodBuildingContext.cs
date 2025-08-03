namespace Respsody.Generators.Syntax;

internal class CommandMethodBuildingContext
{
    public int KeysCount { get; private set; }
    public bool AllArgsAreFixedSize { get; set; }
    public int StaticArgCount { get; private set; }
    public HashSet<OptionSyntaxNode> Options { get; } = [];
    public int IncKeysCount() => KeysCount++;
    public void IncArgCount(int inc) => StaticArgCount+=inc;
    public ResponseType ResponseType { get; set; }
}