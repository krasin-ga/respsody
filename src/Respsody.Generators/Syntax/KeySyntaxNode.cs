using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Generators.Library;

namespace Respsody.Generators.Syntax;

internal class KeySyntaxNode : SyntaxNode
{
    public override ICommandMethodBuilder MethodBuilder { get; }

    public KeySyntaxNode()
    {
        MethodBuilder = new KeyBuilder();
    }

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is KeySyntaxNode;

    protected override int CalculateHashCode()
        => 1;

    public override string ToDisplayString()
        => "key";

    private class KeyBuilder : ICommandMethodBuilder
    {
        private int _keysCount;
        public bool IsArgCountStatic => true;

        private const int ArgCount = 1;

        public void Visit(CommandMethodBuildingContext context)
        {
            _keysCount = context.IncKeysCount();
            context.IncArgCount(ArgCount);
        }
        public string GetParameterName()
        {
            return _keysCount == 0
                ? "key"
                : $"key_{_keysCount}";
        }

        public TypeSyntax GetParameterType()
        {
            return Build.Constants.KeyType;
        }

        public SyntaxTokenList? GetParameterModifiers()
            => TokenList(Token(SyntaxKind.InKeyword));

        public ExpressionSyntax GetCountExpression()
        {
            return LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                Literal(ArgCount));
        }

        public StatementSyntax[] GetBodyStatements()
        {
            var cmd = Build.Constants.CmdVariable;

            return [ParseStatement($"{cmd}.Key({GetParameterName()});")];
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