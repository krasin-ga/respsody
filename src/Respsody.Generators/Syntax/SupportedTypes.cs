using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Respsody.Generators.Syntax;

internal static class SupportedTypes
{
    private static readonly Dictionary<string, TypeSyntax> PrimitiveTypeNameToClrType;

    static SupportedTypes()
    {
        PrimitiveTypeNameToClrType = new Dictionary<string, TypeSyntax>
        {
            { "byte", ParseTypeName("byte") },
            { "byte[]", ParseTypeName("byte[]") },
            { "sbyte", ParseTypeName("sbyte") },
            { "short", ParseTypeName("short") },
            { "ushort", ParseTypeName("ushort") },
            { "int", ParseTypeName("int") },
            { "uint", ParseTypeName("uint") },
            { "long", ParseTypeName("long") },
            { "ulong", ParseTypeName("ulong") },
            { "float", ParseTypeName("float") },
            { "double", ParseTypeName("double") },
            { "decimal", ParseTypeName("decimal") },
            { "object", ParseTypeName("object") },
            { "bool", ParseTypeName("bool") },
            { "char", ParseTypeName("char") },
            { "string", ParseTypeName("string") },
            { "value", ParseTypeName("Value") },
            { "key", ParseTypeName("Key") },
            { "span<char>", ParseTypeName("ReadOnlySpan<char>")},
            { "span<byte>", ParseTypeName("ReadOnlySpan<byte>")}
        };
    }

    public static bool TryGetType(string typeName, out TypeSyntax type)
        => PrimitiveTypeNameToClrType.TryGetValue(typeName, out type);
}