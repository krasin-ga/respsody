using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Generators.Library;

namespace Respsody.Generators.Syntax;

internal class ParameterSyntaxNode(string name, TypeSyntax valueType) : SyntaxNode
{
    public string Name { get; } = name;
    public TypeSyntax ValueType { get; } = valueType;

    public override ICommandMethodBuilder MethodBuilder => new Builder(this);

    public static ParameterSyntaxNode FromRawToken(string rawToken)
    {
        if (rawToken == "value")
            rawToken = "value:value";

        var split = rawToken.Split([":"], StringSplitOptions.RemoveEmptyEntries);
        if (split.Length != 2)
            throw new ArgumentException("Parameter must be in `name:type` format", nameof(rawToken));

        var typeName = split[1];
        if (!SupportedTypes.TryGetType(typeName, out var primitiveType))
            throw new ArgumentNullException($"Failed to parse type from `typeName`", nameof(rawToken));

        var name = split[0];
        return new ParameterSyntaxNode(name, primitiveType);
    }

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is ParameterSyntaxNode parameterSyntaxNode
           && parameterSyntaxNode.Name == Name
           && parameterSyntaxNode.ValueType == ValueType;

    protected override int CalculateHashCode()
    {
        return (Name.GetHashCode() * 97) ^ ValueType.GetHashCode() + GetType().GetHashCode();
    }

    public override string ToDisplayString()
        => $"{Name}:{ValueType}";

    private class Builder(ParameterSyntaxNode node) : ICommandMethodBuilder
    {
        private const int ArgCount = 1;
        public bool IsArgCountStatic => true;

        public void Visit(CommandMethodBuildingContext context)
        {
            context.IncArgCount(ArgCount);
        }

        public string GetParameterName()
            => node.Name.ToValidIdentifier();

        public TypeSyntax GetParameterType() =>
            node.ValueType;

        public SyntaxTokenList? GetParameterModifiers()
        {
            if (node.Name == "value")
                return TokenList(Token(SyntaxKind.InKeyword));

            return null;
        }

        public ExpressionSyntax GetCountExpression()
        {
            return LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                Literal(ArgCount));
        }

        public StatementSyntax[] GetBodyStatements()
        {
            var cmd = Build.Constants.CmdVariable;
            return
            [
                ParseStatement($"{cmd}.Arg({GetParameterName()});")
            ];
        }

        public bool CanReduce(SyntaxNode other)
        {
            return false;
        }

        public StatementSyntax[] GetReducedStatements()
        {
            throw new NotSupportedException();
        }

        public string GetUtf8Representation()
        {
            throw new NotSupportedException();
        }
    }
}