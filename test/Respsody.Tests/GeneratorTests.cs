using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class GeneratorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EscapesLiteralWireValues(bool reduced)
    {
        const string value = "a\"b\\c\té\0";
        var pattern = reduced ? $"ECHO CONSTANT=`{value}` key" : $"ECHO key CONSTANT=`{value}` [key ...]";
        var result = Generate($$"""
            using Respsody;
            using Respsody.Resp;
            namespace Generated;
            [RespCommand({{SymbolDisplay.FormatLiteral(pattern, quote: true)}}, ResponseType.Void)]
            public static class Definitions;
            """);
        AssertCompiles(result);

        var literal = result.Run.GeneratedTrees.Single().GetRoot().DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .First(node => node.IsKind(SyntaxKind.Utf8StringLiteralExpression));
        var probe = CSharpSyntaxTree.ParseText($$"""
            public static class Probe
            {
                public static byte[] Bytes() => ({{literal}}).ToArray();
            }
            """, new CSharpParseOptions(LanguageVersion.CSharp12));
        using var stream = new MemoryStream();
        var emitted = result.Output.AddSyntaxTrees(probe).Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());

        var bulkString = $"${Encoding.UTF8.GetByteCount(value)}\r\n{value}\r\n";
        Assert.Equal(Encoding.UTF8.GetBytes(bulkString),
            (byte[])assembly.GetType("Generated.ECHO+Options+CONSTANT")!
                .GetField("BulkString")!.GetValue(null)!);
        var expectedLiteral = reduced ? $"*3\r\n$4\r\nECHO\r\n{bulkString}" : "*3\r\n$4\r\nECHO\r\n";
        Assert.Equal(Encoding.UTF8.GetBytes(expectedLiteral),
            (byte[])assembly.GetType("Probe")!.GetMethod("Bytes")!.Invoke(null, null)!);
    }

    [Fact]
    public void PreservesPermutedCommandOverloads()
    {
        var result = Generate("""
            using Respsody;
            using Respsody.Client;
            using Respsody.Resp;
            namespace Generated;
            [RespCommand("ORDER A B", ResponseType.Void)]
            [RespCommand("ORDER B A", ResponseType.Void)]
            public static class Definitions;
            public static class Usage
            {
                public static void CompileBoth(IRespClient client)
                {
                    using var first = client.OrderCommand(ORDER.A, ORDER.B);
                    using var second = client.OrderCommand(ORDER.B, ORDER.A);
                }
            }
            """);

        AssertCompiles(result);
        Assert.Equal(2, result.Output.GetTypeByMetadataName("Generated.ORDER")!
            .GetMembers("OrderCommand").Length);
    }

    private static GenerationResult Generate(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(RespCommandAttribute).Assembly.Location)
            .Append(typeof(ResponseType).Assembly.Location)
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path));
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        var compilation = CSharpCompilation.Create(
            "GeneratorRegression_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generatorType = Assembly.Load("Respsody.Generators")
            .GetType("Respsody.Generators.CommandsSourceGenerator", throwOnError: true)!;
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType, nonPublic: true)!;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new GenerationResult(output, driver.GetRunResult(), diagnostics);
    }

    private static void AssertCompiles(GenerationResult result)
    {
        var errors = result.Diagnostics.Concat(result.Output.GetDiagnostics())
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }

    private sealed record GenerationResult(
        Compilation Output,
        GeneratorDriverRunResult Run,
        ImmutableArray<Diagnostic> Diagnostics);
}
