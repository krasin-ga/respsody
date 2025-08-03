using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Respsody.Generators.Library;

namespace Respsody.Generators.Syntax;

internal class CommandSyntaxNode : SyntaxNode
{
    public string Command { get; }

    public override ICommandMethodBuilder MethodBuilder { get; }

    public CommandSyntaxNode(string command)
    {
        Command = command;
        MethodBuilder = new CommandMethodBuilder(this);
    }

    protected override bool IsEquivalentTo(SyntaxNode node)
        => node is CommandSyntaxNode commandSyntaxNode
           && Command == commandSyntaxNode.Command
           && commandSyntaxNode.Children.SequenceEqual(Children);

    protected override int CalculateHashCode()
        => Command.GetHashCode() + Children.UncheckedSum(c => c.GetHashCode());

    public override string ToDisplayString()
        => Command;

    private class CommandMethodBuilder(CommandSyntaxNode node) : ICommandMethodBuilder
    {
        private CommandMethodBuildingContext _context = null!;
        private const int ArgCount = 1;

        public bool IsArgCountStatic => true;

        public void Visit(CommandMethodBuildingContext context)
        {
            context.IncArgCount(ArgCount);

            _context = context;
        }

        public string? GetParameterName() => null;

        public TypeSyntax GetParameterType()
        {
            throw new NotSupportedException();
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
            var client = Build.Constants.ClientParameter.Identifier;
            var cmd = Build.Constants.CmdVariable;
            var argCount = Build.Constants.ArgCount;
            var page = Build.Constants.BufferVariable;

            var commandTypeArgument = _context.ResponseType.MapToTypeName();

            if (_context.AllArgsAreFixedSize)
            {
                var commandWithArraySize = $"*{_context.StaticArgCount}\r\n{GetUtf8RepresentationInternal()}".EscapeLines();

                return
                [
                    ParseStatement($"var {cmd} = {client}.CreateCommand<{commandTypeArgument}>(\"{node.Command}\", @unsafe: true); "),
                    ParseStatement($"var {page} = {cmd}.OutgoingBuffer;"),
                    ParseStatement($"{page}.Write(\"{commandWithArraySize}\"u8);"),
                ];
            }
            
            return
            [
                ParseStatement($"var {cmd} = {client}.CreateCommand<{commandTypeArgument}>(\"{node.Command}\", @unsafe: true); "),
                ParseStatement($"var {page} = {cmd}.OutgoingBuffer;"),
                ParseStatement($"{page}.WriteArraySize({argCount});"),
                ParseStatement($"{page}.Write(CommandBulkString);"),
            ];
        }

        public bool CanReduce(SyntaxNode other)
        {
            return other is OptionSyntaxNode;
        }

        public StatementSyntax[] GetReducedStatements()
        {
            var client = Build.Constants.ClientParameter.Identifier;
            var cmd = Build.Constants.CmdVariable;
            var argCount = Build.Constants.ArgCount;
            var page = Build.Constants.BufferVariable;
            var commandTypeArgument = _context.ResponseType.MapToTypeName();

            if (_context.AllArgsAreFixedSize)
                return
                [
                    ParseStatement($"var {cmd} = {client}.CreateCommand<{commandTypeArgument}>(\"{node.Command}\", @unsafe: true); "),
                    ParseStatement($"var {page} = {cmd}.OutgoingBuffer;"),
                ];

            return
            [
                ParseStatement($"var {cmd} = {client}.CreateCommand<{commandTypeArgument}>(\"{node.Command}\", @unsafe: true); "),
                ParseStatement($"var {page} = {cmd}.OutgoingBuffer;"),
                ParseStatement($"{page}.WriteArraySize({argCount});")
            ];
        }

        public string GetUtf8Representation()
        {
            var cmd = GetUtf8RepresentationInternal();

            if (_context.AllArgsAreFixedSize)
                return $"*{_context.StaticArgCount}\r\n" + cmd;

            return cmd;
        }

        private string GetUtf8RepresentationInternal()
        {
            var byteCount = Encoding.UTF8.GetByteCount(node.Command);
            var cmd = $"${byteCount}\r\n{node.Command}\r\n";
            return cmd;
        }
    }
}