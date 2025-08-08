using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Generators.Syntax;
using SyntaxNode = Respsody.Generators.Syntax.SyntaxNode;

namespace Respsody.Generators.Library;

internal static class Build
{
    public static AttributeListSyntax ClassAttributes()
    {
        return AttributeList(
            SeparatedList<AttributeSyntax>(
                new SyntaxNodeOrToken[]
                {
                    Attribute(Constants.GeneratedCodeAttribute)
                        .WithArgumentList(
                            AttributeArgumentList(
                                SeparatedList<AttributeArgumentSyntax>(
                                    new SyntaxNodeOrToken[]
                                    {
                                        AttributeArgument(
                                            LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                Literal("Respsody"))),
                                        Token(SyntaxKind.CommaToken),
                                        AttributeArgument(
                                            LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                Literal(Constants.GeneratorVersion)))
                                    }))),
                    Token(SyntaxKind.CommaToken),

                    Attribute(Constants.EditorBrowsableAttribute)
                        .WithArgumentList(
                            AttributeArgumentList(
                                SingletonSeparatedList(
                                    AttributeArgument(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    AliasQualifiedName(
                                                        IdentifierName(
                                                            Token(SyntaxKind.GlobalKeyword)),
                                                        IdentifierName("System")),
                                                    IdentifierName("ComponentModel")),
                                                IdentifierName("EditorBrowsableState")),
                                            IdentifierName("Never"))))))
                }));
    }

    public static string MapToTypeName(this ResponseType responseType)
    {
        return responseType switch
        {
            ResponseType.String => "RespString",
            ResponseType.Void => "RespVoid",
            ResponseType.Untyped => "RespResponse",
            ResponseType.Boolean => "RespBoolean",
            ResponseType.Double => "RespDouble",
            ResponseType.Number => "RespNumber",
            ResponseType.BigNumber => "RespBigNumber",
            ResponseType.Array => "RespArray",
            ResponseType.Map => "RespMap",
            ResponseType.Set => "RespSet",
            ResponseType.Subscription => "RespSubscriptionAck",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static MemberDeclarationSyntax CommandBulkStringField(string command)
    {
        return FieldDeclaration(
            VariableDeclaration(ArrayType(ParseTypeName("byte"), List([ArrayRankSpecifier()])))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator("CommandBulkString")
                            .WithInitializer(
                                EqualsValueClause(
                                    ParseExpression(
                                        $"ProtocolWriter.ConvertToBulkString(\"{command}\")"
                                    )))
                    ))
        ).WithModifiers(
            TokenList(
                Token(SyntaxKind.PrivateKeyword),
                Token(SyntaxKind.StaticKeyword),
                Token(SyntaxKind.ReadOnlyKeyword)));
    }

    public static MethodDeclarationSyntax ExtensionMethodForCommandExecution(
        string command,
        bool isPublic,
        string? methodNamePreference,
        ResponseType responseType,
        List<ParameterSyntax> parameters)
    {
        var methodIdentifier = methodNamePreference ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            command.ToLower()
        );

        var method = MethodDeclaration(
                returnType: ParseTypeName(
                    $"ValueTask<{responseType.MapToTypeName()}>"),
                identifier: methodIdentifier)
            .WithModifiers(
                TokenList(isPublic ? Token(SyntaxKind.PublicKeyword) : Token(SyntaxKind.InternalKeyword),
                          Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(
                ParameterList(SeparatedList(parameters))
            );

        var body = Block();

        var client = Constants.ClientParameter.Identifier;
        var cmd = Constants.CmdVariable;

        body = body.AddStatements(
            ParseStatement($"using var {Constants.CmdVariable} = {methodIdentifier}Command({string.Join(",", parameters.Select(p => p.Identifier))});")
            );
        body = body.AddStatements(
            ParseStatement($"return {client}.ExecuteCommand<{responseType.MapToTypeName()}>({cmd}, cancellationToken);")
            );

        return method.WithBody(body);
    }

    public static MethodDeclarationSyntax ExtensionMethodForCommand(
    string command,
    bool isPublic,
    string? methodNamePreference,
    ResponseType responseType,
    List<ParameterSyntax> parameters,
    List<ExpressionSyntax> countExpressions,
    List<List<NodeWithBuilder>> groups,
    bool allArgsFixedSize)
    {
        parameters.Add(
            Parameter(Identifier("cancellationToken"))
                .WithType(ParseTypeName("CancellationToken"))
                .WithDefault(EqualsValueClause(
                                 LiteralExpression(
                                     SyntaxKind.DefaultLiteralExpression,
                                     Token(SyntaxKind.DefaultKeyword))))
        );

        var methodIdentifier = (methodNamePreference ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            command.ToLower()
        )) + "Command";

        var method = MethodDeclaration(
                returnType: ParseTypeName(
                    $"Command<{responseType.MapToTypeName()}>"),
                identifier: methodIdentifier)
            .WithModifiers(
                TokenList(isPublic ? Token(SyntaxKind.PublicKeyword) : Token(SyntaxKind.InternalKeyword),
                          Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(
                ParameterList(SeparatedList(parameters))
            );

        var body = Block();

        if (!allArgsFixedSize)
        {
            var argCountStatement = LocalDeclarationStatement(
                VariableDeclaration(
                        PredefinedType(
                            Token(SyntaxKind.IntKeyword)))
                    .WithVariables(
                        SingletonSeparatedList(
                            VariableDeclarator(Constants.ArgCount)
                                .WithInitializer(
                                    EqualsValueClause(
                                        countExpressions.Skip(1).Aggregate(
                                            countExpressions[0],
                                            (a, b) => BinaryExpression(SyntaxKind.AddExpression, a, b)
                                        )
                                    )))));

            body = body.AddStatements(
                argCountStatement);
        }

        var bodyStatements = new List<StatementSyntax>();
        var page = Constants.BufferVariable;

        foreach (var group in groups)
        {
            if (group.Count > 1)
            {
                var sb = new StringBuilder();
                foreach (var nodeWithBuilder in group)
                {
                    bodyStatements.AddRange(nodeWithBuilder.Builder.GetReducedStatements());
                    sb.Append(nodeWithBuilder.Builder.GetUtf8Representation());
                }

                var text = $"{page}.Write(\"{sb}\"u8);";

                text = text.EscapeLines();

                bodyStatements.Add(
                    ParseStatement(text)
                );

                continue;
            }

            bodyStatements.AddRange(
                group[0].Builder.GetBodyStatements()
            );
        }

        var cmd = Constants.CmdVariable;

        body = body.AddStatements([.. bodyStatements]);
        body = body.AddStatements(ParseStatement($"return {cmd};"));

        return method.WithBody(body);
    }


    public static string EscapeLines(this string text)
    {
        return text.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    public static ClassDeclarationSyntax OptionTypes(
        HashSet<OptionSyntaxNode> options,
        ClassDeclarationSyntax @class)
    {
        var optionsClass = ClassDeclaration("Options")
            .AddModifiers(Token(SyntaxKind.PublicKeyword));

        foreach (var option in options)
        {
            var structDeclaration = StructDeclaration(option.Option).WithModifiers(
                TokenList(
                    Token(SyntaxKind.PublicKeyword)
                )
            );

            structDeclaration = structDeclaration.AddMembers(
                FieldDeclaration(
                        VariableDeclaration(ArrayType(ParseTypeName("byte"), List([ArrayRankSpecifier()])))
                            .WithVariables(
                                SingletonSeparatedList(
                                    VariableDeclarator("BulkString")
                                        .WithInitializer(
                                            EqualsValueClause(
                                                ParseExpression(
                                                    $"ProtocolWriter.ConvertToBulkString(\"{option.Value}\")"
                                                )))
                                )))
                    .WithModifiers(
                        TokenList(
                            Token(SyntaxKind.PublicKeyword),
                            Token(SyntaxKind.StaticKeyword),
                            Token(SyntaxKind.ReadOnlyKeyword)))
            );

            optionsClass = optionsClass.AddMembers(
                structDeclaration
            );

            var typeSyntax = ParseTypeName($"Options.{option.Option}");

            var variable = VariableDeclarator(option.Option)
                .WithInitializer(
                    EqualsValueClause(
                        ObjectCreationExpression(
                            typeSyntax
                        ).WithArgumentList(ArgumentList())
                    ));

            @class = @class.AddMembers(
                FieldDeclaration(
                        VariableDeclaration(typeSyntax).WithVariables(
                            SingletonSeparatedList(variable)))
                    .WithModifiers(
                        TokenList(
                            Token(SyntaxKind.PublicKeyword),
                            Token(SyntaxKind.StaticKeyword),
                            Token(SyntaxKind.ReadOnlyKeyword)))
            );
        }

        @class = @class.AddMembers(optionsClass);
        return @class;
    }

    public static class ForStatement
    {
        public static ForStatementSyntax ForArray(
            string elementVariableName,
            string indexVariableName,
            string arrayVariableName,
            IEnumerable<StatementSyntax> statementSyntaxes)
        {
            var statements = SingletonList<StatementSyntax>(
                LocalDeclarationStatement(
                    VariableDeclaration(
                            IdentifierName("var"))
                        .WithVariables(
                            SingletonSeparatedList(
                                VariableDeclarator(
                                        Identifier(elementVariableName))
                                    .WithInitializer(
                                        EqualsValueClause(
                                            ElementAccessExpression(
                                                    IdentifierName(arrayVariableName))
                                                .WithArgumentList(
                                                    BracketedArgumentList(
                                                        SingletonSeparatedList(
                                                            Argument(
                                                                IdentifierName(indexVariableName)))))))))));
            statements = statements.AddRange(statementSyntaxes);

            return ForStatement(
                    Block(
                        statements))
                .WithDeclaration(
                    VariableDeclaration(
                            IdentifierName("var"))
                        .WithVariables(
                            SingletonSeparatedList(
                                VariableDeclarator(
                                        Identifier(indexVariableName))
                                    .WithInitializer(
                                        EqualsValueClause(
                                            LiteralExpression(
                                                SyntaxKind.NumericLiteralExpression,
                                                Literal(0)))))))
                .WithCondition(
                    BinaryExpression(
                        SyntaxKind.LessThanExpression,
                        IdentifierName(indexVariableName),
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(arrayVariableName),
                            IdentifierName("Length"))))
                .WithIncrementors(
                    SingletonSeparatedList<ExpressionSyntax>(
                        PostfixUnaryExpression(
                            SyntaxKind.PostIncrementExpression,
                            IdentifierName(indexVariableName))));
        }
    }

    public static class Constants
    {
        public static readonly TypeSyntax KeyType = ParseTypeName("Respsody.Key");
        public static readonly TypeSyntax KeysType = ParseTypeName("Respsody.Keys");
        public static readonly SyntaxToken ArgCount = Identifier("rspArgs");
        public static readonly SyntaxToken CmdVariable = Identifier("rspCmd");
        public static readonly SyntaxToken BufferVariable = Identifier("rspBfr");

        public static readonly QualifiedNameSyntax GeneratedCodeAttribute = QualifiedName(
            QualifiedName(
                QualifiedName(
                    AliasQualifiedName(
                        IdentifierName(
                            Token(SyntaxKind.GlobalKeyword)),
                        IdentifierName("System")),
                    IdentifierName("CodeDom")),
                IdentifierName("Compiler")),
            IdentifierName("GeneratedCodeAttribute"));

        public static readonly QualifiedNameSyntax EditorBrowsableAttribute = QualifiedName(
            QualifiedName(
                AliasQualifiedName(
                    IdentifierName(
                        Token(SyntaxKind.GlobalKeyword)),
                    IdentifierName("System")),
                IdentifierName("ComponentModel")),
            IdentifierName("EditorBrowsableAttribute"));

        public static readonly ParameterSyntax ClientParameter =
            Parameter(
                    Identifier("respClient"))
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.ThisKeyword)))
                .WithType(ParseTypeName("IRespClient"));

        public static readonly string GeneratorVersion = typeof(Build).Assembly.GetName().Version.ToString();
    }

    public static class Type
    {
        public static TypeSyntax For(SyntaxNode node)
        {
            switch (node)
            {
                case OptionSyntaxNode optionSyntaxNode:
                    return ParseTypeName(optionSyntaxNode.Option);
                case KeySyntaxNode:
                    return ParseTypeName("Key");
                case ParameterSyntaxNode parameterSyntax:
                    return parameterSyntax.ValueType;
                default:
                    throw new ArgumentOutOfRangeException(nameof(node));
            }
        }
    }

    public static class Parameters
    {
        public static ParameterSyntax ForArray(ArraySyntaxNode arraySyntaxNode, HashSet<OptionSyntaxNode> options, ref int keyCount)
        {
            if (arraySyntaxNode.Children.Count == 1)
            {
                if (arraySyntaxNode.TryGetSingleChildOfType<KeySyntaxNode>(out _))
                    return ForKeys(ref keyCount);

                if (arraySyntaxNode.TryGetSingleChildOfType<OptionSyntaxNode>(out var option))
                {
                    options.Add(option);
                    return ForOptions(option);
                }

                if (arraySyntaxNode.TryGetSingleChildOfType<ParameterSyntaxNode>(out var parameter))
                    return ForParameters(parameter);

                throw new ArgumentOutOfRangeException(nameof(arraySyntaxNode));
            }

            var elements = new List<SyntaxNodeOrToken>(arraySyntaxNode.Children.Count * 2);

            foreach (var child in arraySyntaxNode.Children)
            {
                if (child is OptionSyntaxNode optionSyntaxNode)
                    options.Add(optionSyntaxNode);

                elements.Add(TupleElement(Type.For(child)));
                elements.Add(Token(SyntaxKind.CommaToken));
            }

            elements.RemoveAt(elements.Count - 1);

            var type = TupleType(SeparatedList<TupleElementSyntax>(elements));
            return Parameter(Identifier("array"))
                .WithType(ArrayType(type, List([ArrayRankSpecifier()])));
        }

        public static ParameterSyntax ForOption(OptionSyntaxNode optionSyntaxNode)
        {
            return Parameter(Identifier(optionSyntaxNode.Option.ToLower()))
                .WithType(Type.For(optionSyntaxNode));
        }

        public static ParameterSyntax ForOptions(OptionSyntaxNode option)
        {
            return Parameter(Identifier(option.Option.ToLower() + "Items")).WithType(
                ArrayType(Type.For(option), List([ArrayRankSpecifier()]))
            );
        }

        public static ParameterSyntax ForParameter(ParameterSyntaxNode parameterSyntaxNode)
        {
            return Parameter(Identifier(parameterSyntaxNode.Name))
                .WithType(Type.For(parameterSyntaxNode));
        }

        public static ParameterSyntax ForParameters(ParameterSyntaxNode parameterSyntaxNode)
        {
            return Parameter(Identifier($"{parameterSyntaxNode.Name}Items")).WithType(
                ArrayType(
                    Type.For(parameterSyntaxNode),
                    List([ArrayRankSpecifier()]))
            );
        }

        public static ParameterSyntax ForKey(ref int keyCount)
        {
            var name = keyCount++ == 0
                ? "key"
                : $"key_{keyCount - 1}";

            return Parameter(Identifier(name))
                .WithType(Constants.KeyType);
        }

        public static ParameterSyntax ForKeys(ref int keyCount)
        {
            var name = keyCount++ == 0
                ? "keys"
                : $"keys_{keyCount - 1}";

            return Parameter(Identifier(name)).WithType(
                ArrayType(Constants.KeyType, List([ArrayRankSpecifier()]))
            );
        }
    }
}