using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Generators.Library;

namespace Respsody.Generators.Syntax;

internal class ArraySyntaxNode : VariedSyntaxNode
{
    public override ICommandMethodBuilder MethodBuilder { get; }

    public ArraySyntaxNode()
    {
        MethodBuilder = new ArrayNodeMethodBuilder(this);
    }

    public override IEnumerable<IEnumerable<SyntaxNode>> GetVariations()
    {
        yield break;
    }

    public bool TryGetSingleChildOfType<T>(out T value)
    {
        if (Children.Count != 1)
        {
            value = default!;
            return false;
        }

        var child = Children[0];
        if (child is not T tChild)
        {
            value = default!;
            return false;
        }

        value = tChild;
        return true;
    }

    public override void AddChild(SyntaxNode node)
    {
        if (node is not (OptionSyntaxNode or ParameterSyntaxNode or KeySyntaxNode))
            throw new ArgumentException();

        base.AddChild(node);
    }

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is ArraySyntaxNode paramsSyntaxNode
           && paramsSyntaxNode.Children.SequenceEqual(Children);

    public override string ToDisplayString()
        => $"array({string.Join(" ", Children.Select(c => c.ToDisplayString()))})";

    /// <summary>
    /// a [a ...] -> [a ...]
    /// </summary>
    public void TryRemoveExplicitArguments(List<SyntaxNode> explication)
    {
        if (explication.Count < Children.Count)
            return;

        for (var i = 0; i < Children.Count; i++)
            if (!explication[explication.Count - Children.Count + i].Equals(Children[i]))
                return;

        for (var i = 0; i < Children.Count; i++)
            explication.RemoveAt(explication.Count - 1);
    }

    public class ArrayNodeMethodBuilder(ArraySyntaxNode arraySyntaxNode) : ICommandMethodBuilder
    {
        public bool IsArgCountStatic => false;

        public void Visit(CommandMethodBuildingContext context)
        {
            foreach (var syntaxNode in arraySyntaxNode.Children)
                if (syntaxNode is OptionSyntaxNode optionSyntaxNode)
                    context.Options.Add(optionSyntaxNode);
        }

        public string GetParameterName()
        {
            if (arraySyntaxNode.Children.Count == 1)
            {
                if (arraySyntaxNode.TryGetSingleChildOfType<KeySyntaxNode>(out var keySyntaxNode))
                    return NameFor(keySyntaxNode);

                if (arraySyntaxNode.TryGetSingleChildOfType<OptionSyntaxNode>(out var option))
                    return NameFor(option);

                if (arraySyntaxNode.TryGetSingleChildOfType<ParameterSyntaxNode>(out var parameter))
                    return NameFor(parameter);

                throw new ArgumentOutOfRangeException(nameof(arraySyntaxNode));
            }

            var name = new StringBuilder();

            for (var i = 0; i < arraySyntaxNode.Children.Count; i++)
            {
                var child = arraySyntaxNode.Children[i];
                name.Append(
                    i > 0
                        ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(child.MethodBuilder.GetParameterName()!)
                        : child.MethodBuilder.GetParameterName()
                );
            }

            name.Append("Array");

            return name.ToString();
        }

        public SyntaxTokenList? GetParameterModifiers()
            => null;

        public TypeSyntax GetParameterType()
        {
            if (arraySyntaxNode.Children.Count == 1)
            {
                if (arraySyntaxNode.TryGetSingleChildOfType<KeySyntaxNode>(out var keySyntaxNode))
                    return ArrayOf(Build.Constants.KeyType);

                if (arraySyntaxNode.TryGetSingleChildOfType<OptionSyntaxNode>(out var option))
                    return ArrayOf(option);

                if (arraySyntaxNode.TryGetSingleChildOfType<ParameterSyntaxNode>(out var parameter))
                    return ArrayOf(parameter);

                throw new ArgumentOutOfRangeException(nameof(arraySyntaxNode));
            }

            var elements = new List<SyntaxNodeOrToken>(arraySyntaxNode.Children.Count * 2);

            foreach (var child in arraySyntaxNode.Children)
            {
                elements.Add(TupleElement(child.MethodBuilder.GetParameterType()));
                elements.Add(Token(SyntaxKind.CommaToken));
            }

            elements.RemoveAt(elements.Count - 1);

            return ArrayOf(
                TupleType(SeparatedList<TupleElementSyntax>(elements)));
        }

        private static ArrayTypeSyntax ArrayOf(TypeSyntax typeSyntax)
        {
            return ArrayType(typeSyntax, List([ArrayRankSpecifier()]));
        }

        private static ArrayTypeSyntax ArrayOf(SyntaxNode syntaxNode)
        {
            return ArrayType(
                syntaxNode.MethodBuilder.GetParameterType(),
                List([ArrayRankSpecifier()])
            );
        }

        private static string NameFor(SyntaxNode syntaxNode)
        {
            return $"{syntaxNode.MethodBuilder.GetParameterName()}Array";
        }

        public ExpressionSyntax GetCountExpression()
        {
            return ParseExpression($"({GetParameterName()}.Length * {arraySyntaxNode.Children.Count})");
        }

        public StatementSyntax[] GetBodyStatements()
        {
            var statements = new List<StatementSyntax>();
            const string current = "rspCurrentElement";
            var cmd = Build.Constants.CmdVariable;

            for (var i = 0; i < arraySyntaxNode.Children.Count; i++)
            {
                var child = arraySyntaxNode.Children[i];
                if (child is OptionSyntaxNode optionSyntaxNode)
                {
                    statements.AddRange(optionSyntaxNode.MethodBuilder.GetBodyStatements());
                    continue;
                }

                statements.Add(
                    arraySyntaxNode.Children.Count == 1
                ? ParseStatement($"{cmd}.Arg({current});")
                : ParseStatement($"{cmd}.Arg({current}.Item{i+1});")
                    );
            }

            var statement = Build.ForStatement.ForArray(
                current,
                "rspIdx",
                GetParameterName(),
                statements.ToArray()
            );

            return [statement];
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