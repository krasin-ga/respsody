using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Respsody.Generators.Syntax;

internal interface ICommandMethodBuilder
{
    public bool IsArgCountStatic { get; }
    public void Visit(CommandMethodBuildingContext context);
    string? GetParameterName();
    SyntaxTokenList? GetParameterModifiers();
    TypeSyntax GetParameterType();
    ExpressionSyntax GetCountExpression();
    StatementSyntax[] GetBodyStatements();


    public bool CanReduce(SyntaxNode other);
    StatementSyntax[] GetReducedStatements();
    public string GetUtf8Representation();
}