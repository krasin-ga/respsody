using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Generators.Library;
using Respsody.Generators.Syntax;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace Respsody.Generators;

[Generator(LanguageNames.CSharp)]
internal class CommandsSourceGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "RespCommandAttribute";
    private const string AttributeName = "RespCommand";
    private const string AttributeNamespace = "Respsody";

    private static readonly DiagnosticDescriptor ErrorRule = new(
        id: "CommandsSourceGeneratorError",
        title: "RSP1001: RESP command generation failed",
        messageFormat: "Generation failed because of exception: {0}",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contextSyntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
            (syntaxNode, _) => IsCommandDefinitionClass(syntaxNode),
            (ctx, _) => (ctx.Node, ctx.SemanticModel));

        var commandContexts = contextSyntaxProvider.SelectMany(
            (v, ct) => CreateRespCommandContext(v.Node, v.SemanticModel, ct)
        );

        var parsingErrors = commandContexts.Where(ctx => ctx.ErrorContext is { })
            .Select((ctx, _) => ctx.ErrorContext!);

        var parsedCommandsGroups = commandContexts.Where(ctx => ctx.Context is { })
            .Select((ctx, _) => ctx.Context!)
            .Collect()
            .SelectMany((c, _) => c.GroupBy(g => (g!.CommandSyntaxNode.Command, g!.Namespace)))
            .Select((g, _) => g.ToArray());

        context.RegisterSourceOutput(
            parsingErrors,
            (spc, ctx) => spc.ReportDiagnostic(
                Diagnostic.Create(ErrorRule, ctx.Location, ctx.Exception)
            ));

        context.RegisterSourceOutput(parsedCommandsGroups, CreateOutput);
    }

    private static void CreateOutput(
        SourceProductionContext sourceProductionContext,
        RespCommandContext[] commandGrouping)
    {
        var explicationsWithCtx = new List<ExplicationWithContext>();
        var visitedExplications = new HashSet<(CommandNodeExplication.Explication, string)>();
        var orderedGrouping = commandGrouping.OrderByDescending(
            c => c.ExplicitPriority ?? int.MinValue + c.Location.SourceSpan.Start
        );

        foreach (var ctx in orderedGrouping)
        foreach (var explication in new CommandNodeExplication(ctx.CommandSyntaxNode).TraverseWithStack())
        {
            if (visitedExplications.Add((explication, ctx.MethodNamePreference ?? "")))
                explicationsWithCtx.Add(new ExplicationWithContext(explication, ctx));

            var voidCtx = ctx.ToVoidResponseContext();
            if (voidCtx is null)
                continue;

            if (visitedExplications.Add((explication, voidCtx.MethodNamePreference ?? "")))
                explicationsWithCtx.Add(new ExplicationWithContext(explication, voidCtx));
        }

        var options = new HashSet<OptionSyntaxNode>();

        var command = commandGrouping[0].CommandSyntaxNode.Command;
        var ns = commandGrouping[0].Namespace;

        var isClassPublic = commandGrouping.Any(c => c.IsPublic);

        var @class = ClassDeclaration(command)
            .AddModifiers(isClassPublic
                              ? Token(SyntaxKind.PublicKeyword)
                              : Token(SyntaxKind.InternalKeyword),
                          Token(SyntaxKind.StaticKeyword))
            .AddMembers(Build.CommandBulkStringField(command));

        foreach (var (explication, commandContext) in explicationsWithCtx)
        {
            List<ParameterSyntax> parameters = [Build.Constants.ClientParameter];

            var context = new CommandMethodBuildingContext { ResponseType = commandContext.ResponseType };
            var countExpressions = new List<ExpressionSyntax>();
            var allArgsFixedSize = true;
            var builders = explication.Select(node => (Node: node, Builder: node.MethodBuilder)).ToArray();

            var reducedGroups = new List<List<NodeWithBuilder>>();

            foreach (var (node, builder) in builders)
            {
                builder.Visit(context);
                allArgsFixedSize &= builder.IsArgCountStatic;

                if (builder.GetParameterName() is { } parameterName)
                    parameters.Add(
                        Parameter(Identifier(parameterName))
                            .WithType(builder.GetParameterType())
                            .WithModifiers(builder.GetParameterModifiers() ?? [])
                    );

                countExpressions.Add(builder.GetCountExpression());

                if (reducedGroups.Count == 0)
                {
                    reducedGroups.Add([new NodeWithBuilder(node, builder)]);
                    continue;
                }

                var lastGroup = reducedGroups[reducedGroups.Count - 1];
                var lastInGroup = lastGroup[lastGroup.Count - 1];
                if (lastInGroup.Builder.CanReduce(node) && builder.CanReduce(lastInGroup.Node))
                {
                    lastGroup.Add(new NodeWithBuilder(node, builder));
                    continue;
                }

                reducedGroups.Add([new NodeWithBuilder(node, builder)]);
            }

            context.AllArgsAreFixedSize = allArgsFixedSize;

            @class = @class.AddMembers(
                Build.ExtensionMethodForCommand(
                    command: command,
                    isPublic: commandContext.IsPublic,
                    methodNamePreference: commandContext.MethodNamePreference,
                    responseType: commandContext.ResponseType,
                    parameters: parameters,
                    countExpressions: countExpressions,
                    groups: reducedGroups,
                    allArgsFixedSize: allArgsFixedSize),
                Build.ExtensionMethodForCommandExecution(
                    command: command,
                    isPublic: commandContext.IsPublic,
                    methodNamePreference: commandContext.MethodNamePreference,
                    responseType: commandContext.ResponseType,
                    parameters: parameters)
            );

            options.UnionWith(context.Options);
        }

        @class = Build.OptionTypes(options, @class);
        //@class = @class.WithAttributeLists(
        //    new SyntaxList<AttributeListSyntax>(Build.ClassAttributes())
        //    );

        var @namespace = NamespaceDeclaration(IdentifierName(ns))
            .AddMembers(@class);

        var compilationUnit = CompilationUnit()
            .AddUsings(
                UsingDirective(IdentifierName("System")),
                UsingDirective(IdentifierName("System.Threading")),
                UsingDirective(IdentifierName("System.Threading.Tasks")),
                UsingDirective(IdentifierName("Respsody.Client")),
                UsingDirective(IdentifierName("Respsody.Resp")),
                UsingDirective(IdentifierName("Respsody")))
            .AddMembers(@namespace);

        var code = compilationUnit.NormalizeWhitespace().ToFullString();

        sourceProductionContext.AddSource(
            $"{ns.GetSafeName()}.{command.GetSafeName()}.cs",
            code);
    }

    private static IEnumerable<(RespCommandContext? Context, ErrorContext? ErrorContext)> CreateRespCommandContext(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        (RespCommandContext? Context, ErrorContext? ErrorContext) Error(Exception exception)
            => (
                null,
                new ErrorContext(exception, node.GetLocation())
            );

        var result = new List<(RespCommandContext? Context, ErrorContext? ErrorContext)>();

        try
        {
            if (semanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol symbol)
                throw new InvalidCastException("Node is not NamedTypeSyntax");

            var attributes = symbol.GetAttributes().Where(
                a => a.AttributeClass?.ContainingNamespace.Name == AttributeNamespace
                     && a.AttributeClass.Name == AttributeFullName);

            foreach (var attribute in attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attribute is null || attribute.ConstructorArguments.Length != 2)
                {
                    result.Add(Error(new FormatException("Invalid format of attribute")));
                    continue;
                }

                var cmdArg = attribute.ConstructorArguments[0];
                var typeArg = attribute.ConstructorArguments[1];
                if (cmdArg.Value is not string commandPattern
                    || typeArg.Kind != TypedConstantKind.Enum || typeArg.Value is null)
                {
                    result.Add(Error(new FormatException("Invalid format of attribute")));
                    continue;
                }

                var methodName = attribute.NamedArguments.FirstOrDefault(
                    a => a.Key == "MethodName"
                ).Value.Value as string;

                var explicitPriority = attribute.NamedArguments.FirstOrDefault(
                    a => a.Key == "ExplicitPriority"
                ).Value.Value as int?;

                var isPublic = true;

                if (attribute.NamedArguments.FirstOrDefault(a => a.Key == "Visibility") is var visibility)
                    if (visibility.Value.Kind == TypedConstantKind.Enum
                        && (Visibility)visibility.Value.Value! == Visibility.Internal)
                        isPublic = false;

                var @namespace = symbol.ContainingNamespace.IsGlobalNamespace
                    ? symbol.ContainingAssembly.Name
                    : symbol.ContainingNamespace.ToDisplayString();

                result.Add(
                    (
                        Context: new RespCommandContext(
                            symbol,
                            commandPattern,
                            @namespace,
                            (ResponseType)typeArg.Value!,
                            methodName,
                            explicitPriority,
                            isPublic,
                            node.GetLocation()
                        ),
                        ErrorContext: null
                    ));
            }
        }
        catch (Exception exception)
        {
            result.Add(Error(exception));
        }

        return result;
    }

    private static bool IsCommandDefinitionClass(SyntaxNode syntaxNode)
    {
        if (syntaxNode is not ClassDeclarationSyntax classDeclaration)
            return false;

        return classDeclaration.Modifiers.Any(m => m.Text is "static")
               && classDeclaration.AttributeLists.Any(
                   attrList => attrList.Attributes
                       .Any(attr => attr.Name is IdentifierNameSyntax { Identifier.Text: AttributeName or AttributeFullName }
                                or QualifiedNameSyntax { Right.Identifier.Text: AttributeName or AttributeFullName }));
    }

    private class ErrorContext(Exception exception, Location location)
    {
        public Exception Exception { get; } = exception;
        public Location Location { get; } = location;
    }

    private readonly struct ExplicationWithContext(
        CommandNodeExplication.Explication explication,
        RespCommandContext context)
    {
        public CommandNodeExplication.Explication Explication { get; } = explication;
        public RespCommandContext Context { get; } = context;

        public void Deconstruct(out CommandNodeExplication.Explication explication, out RespCommandContext context)
        {
            explication = Explication;
            context = Context;
        }
    }

    private class RespCommandContext(
        INamedTypeSymbol targetSymbol,
        string rawSyntax,
        string @namespace,
        ResponseType responseType,
        string? methodNamePreference,
        int? explicitPriority,
        bool isPublic,
        Location location)
    {
        public INamedTypeSymbol TargetSymbol { get; } = targetSymbol;
        public string RawSyntax { get; } = rawSyntax;
        public CommandSyntaxNode CommandSyntaxNode { get; } = CommandSyntaxParser.Parse(rawSyntax);
        public string Namespace { get; } = @namespace;
        public Location Location { get; } = location;
        public int? ExplicitPriority { get; } = explicitPriority;
        public bool IsPublic { get; } = isPublic;
        public ResponseType ResponseType { get; } = responseType;
        public string? MethodNamePreference { get; } = methodNamePreference;

        public RespCommandContext? ToVoidResponseContext()
        {
            if (ResponseType == ResponseType.Void)
                return null;

            return new RespCommandContext(
                targetSymbol: TargetSymbol,
                rawSyntax: RawSyntax,
                @namespace: Namespace,
                responseType: ResponseType.Void,
                methodNamePreference: MethodNamePreference is { }
                    ? MethodNamePreference + "Void"
                    : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                        CommandSyntaxNode.Command.ToLower()
                    ) + "Void",
                explicitPriority: ExplicitPriority,
                isPublic: IsPublic,
                location: Location);
        }
    }
}