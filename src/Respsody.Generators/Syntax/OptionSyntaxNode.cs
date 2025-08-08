using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Generators.Library;

namespace Respsody.Generators.Syntax;

/// Example: LIMIT offset count
/// 
/// LIMIT - OptionSyntaxNode
///
/// INCREMENT_SCRIPT=`some value` - OptionSyntaxNode with predefined value
internal class OptionSyntaxNode : SyntaxNode
{
    private static readonly Regex FormatRegex = new("[A-Z_]+(=`.+?`)?", RegexOptions.Compiled);
    public string Option { get; }
    public string Value { get; }

    public override ICommandMethodBuilder MethodBuilder { get; }

    public OptionSyntaxNode(string option)
    {
        var split = option.Split(['='], 2, StringSplitOptions.None);
        Option = split[0];
        Value = split.Length > 1 ? split[1].Trim('`') : Option;

        MethodBuilder = new OptionMethodBuilder(this);
    }

    public static bool MatchFormat(string str)
    {
        return FormatRegex.Match(str).Length == str.Length;
    }

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is OptionSyntaxNode optionSyntaxNode && optionSyntaxNode.Option == Option;

    protected override int CalculateHashCode()
        => Option.GetHashCode();

    public override string ToDisplayString()
        => Option;

    private class OptionMethodBuilder(OptionSyntaxNode node) : ICommandMethodBuilder
    {
        private const int ArgCount = 1;
        public bool IsArgCountStatic => true;

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
            var option = node.Value;

            var byteCount = Encoding.UTF8.GetByteCount(option);
            return $"${byteCount}\r\n{option}\r\n";
        }
    }
}