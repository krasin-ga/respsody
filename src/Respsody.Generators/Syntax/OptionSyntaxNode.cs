using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Generators.Library;

namespace Respsody.Generators.Syntax;

/// Example: LIMIT offset count
/// 
/// LIMIT - OptionSyntaxNode
internal class OptionSyntaxNode : SyntaxNode
{
    public OptionSyntaxNode(string option)
    {
        Option = option;
        MethodBuilder = new OptionMethodBuilder(this);
    }

    public string Option { get; }

    public override ICommandMethodBuilder MethodBuilder { get; }

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is OptionSyntaxNode optionSyntaxNode && optionSyntaxNode.Option == Option;

    protected override int CalculateHashCode()
        => Option.GetHashCode();

    public override string ToDisplayString()
        => Option;


    private class OptionMethodBuilder(OptionSyntaxNode node) : ICommandMethodBuilder
    {
        public bool IsArgCountStatic => true;
        private const int ArgCount = 1;

        public void Visit(CommandMethodBuildingContext context)
        {
            context.Options.Add(node);
            context.IncArgCount(ArgCount);
        }

        public string GetParameterName()
        {
            return node.Option.ToLower();
        }

        public TypeSyntax GetParameterType()
        {
            return ParseTypeName($"Options.{node.Option}");
        }

        public SyntaxTokenList? GetParameterModifiers()
            => null;

        public ExpressionSyntax GetCountExpression()
        {
            return LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                Literal(ArgCount));
        }

        public StatementSyntax[] GetBodyStatements()
        {
            var page = Build.Constants.BufferVariable;

            return [ParseStatement($"{page}.Write({GetParameterType()}.BulkString);")];
        }

        public bool CanReduce(SyntaxNode other)
        {
            return other is OptionSyntaxNode or CommandSyntaxNode;
        }

        public StatementSyntax[] GetReducedStatements()
        {
            return [];
        }

        public string GetUtf8Representation()
        {
            var option = node.Option;

            var byteCount = Encoding.UTF8.GetByteCount(option);
            return $"${byteCount}\r\n{option}\r\n";
        }
    }
}