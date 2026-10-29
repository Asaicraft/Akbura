using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes the standard fully qualified C# type display without an intermediate name string.
/// Unusual displays are delegated to Roslyn before any part of the name is written.
/// </summary>
internal static class SymbolDisplayCodeWriterExtension
{
    private static readonly FullyQualifiedNameVisitor s_visitor = new();

    public static CodeWriter WriteFullyQualifiedName(this CodeWriter writer, ITypeSymbol symbol)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }
        if (symbol is null)
        {
            throw new ArgumentNullException(nameof(symbol));
        }

        if (CachedSymbolDisplay.TryGetFullyQualifiedName(symbol, out var name))
        {
            return writer.Write(name);
        }
        if (!CanWrite(symbol, 0))
        {
            return writer.Write(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        s_visitor.Visit(symbol, writer);
        return writer;
    }

    private static bool CanWrite(ITypeSymbol symbol, int depth)
    {
        if (depth > 64 || symbol.Language != LanguageNames.CSharp)
        {
            return false;
        }

        switch (symbol)
        {
            case IDynamicTypeSymbol:
                return true;
            case ITypeParameterSymbol parameter:
                return CanWriteIdentifier(parameter.Name);
            case IArrayTypeSymbol array:
                return array.CustomModifiers.IsEmpty && CanWrite(array.ElementType, depth + 1);
            case IPointerTypeSymbol pointer:
                return pointer.CustomModifiers.IsEmpty && CanWrite(pointer.PointedAtType, depth + 1);
            case INamedTypeSymbol named:
                if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate) ||
                    named.IsAnonymousType ||
                    !CanWriteIdentifier(named.Name))
                {
                    return false;
                }
                if (named.ContainingType is { } containing)
                {
                    if (!CanWrite(containing, depth + 1)) return false;
                }
                else if (!CanWriteNamespace(named.ContainingNamespace, depth + 1))
                {
                    return false;
                }
                if (named.IsTupleType)
                {
                    if (!CanUseTupleSyntax(named)) return false;
                    foreach (var field in named.TupleElements)
                    {
                        if (!CanWrite(field.Type, depth + 1) || !CanWriteIdentifier(field.Name)) return false;
                    }
                    return true;
                }
                for (var i = 0; i < named.TypeArguments.Length; i++)
                {
                    if (!named.GetTypeArgumentCustomModifiers(i).IsEmpty ||
                        (!named.IsUnboundGenericType && !CanWrite(named.TypeArguments[i], depth + 1)))
                    {
                        return false;
                    }
                }
                return true;
            default:
                // Function pointers, error/anonymous types and foreign-language symbols
                // use the complete Roslyn display, including calling conventions.
                return false;
        }
    }

    private static bool CanWriteNamespace(INamespaceSymbol? symbol, int depth) =>
        symbol is null || symbol.IsGlobalNamespace ||
        (depth <= 64 && CanWriteIdentifier(symbol.Name) && CanWriteNamespace(symbol.ContainingNamespace, depth + 1));

    private static bool CanWriteIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.IsValidIdentifier(name);

    private static bool CanUseTupleSyntax(INamedTypeSymbol symbol)
    {
        var current = symbol.TupleUnderlyingType ?? symbol;
        if (current.Arity < 2) return false;
        while (true)
        {
            for (var i = 0; i < current.TypeArguments.Length; i++)
            {
                if (!current.GetTypeArgumentCustomModifiers(i).IsEmpty) return false;
            }
            if (current.Arity != 8) return true;
            if (current.TypeArguments[7] is not INamedTypeSymbol { IsTupleType: true } rest ||
                rest.TypeKind == TypeKind.Error)
            {
                return false;
            }
            // Friendly names in an explicitly constructed Rest can require ValueTuple syntax.
            // Conservatively delegate even explicitly named default Rest elements to Roslyn.
            foreach (var field in rest.TupleElements)
            {
                if (field.IsExplicitlyNamedTupleElement) return false;
            }
            current = rest.TupleUnderlyingType ?? rest;
        }
    }

    private static void WriteIdentifier(CodeWriter writer, string name)
    {
        // FullyQualifiedFormat escapes reserved keywords, not contextual keywords.
        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None) writer.Write("@");
        writer.Write(name);
    }

    private sealed class FullyQualifiedNameVisitor : SymbolVisitor<CodeWriter, bool>
    {
        protected override bool DefaultResult => false;

        public override bool VisitDynamicType(IDynamicTypeSymbol symbol, CodeWriter writer)
        {
            writer.Write("dynamic");
            return true;
        }

        public override bool VisitTypeParameter(ITypeParameterSymbol symbol, CodeWriter writer)
        {
            WriteIdentifier(writer, symbol.Name);
            return true;
        }

        public override bool VisitArrayType(IArrayTypeSymbol symbol, CodeWriter writer)
        {
            ITypeSymbol element = symbol;
            while (element is IArrayTypeSymbol array) element = array.ElementType;
            Visit(element, writer);
            // C# rank specifiers run from the outermost array towards its element.
            for (IArrayTypeSymbol? array = symbol; array != null; array = array.ElementType as IArrayTypeSymbol)
            {
                writer.Write("[");
                if (array.Rank == 1 && !array.IsSZArray) writer.Write("*");
                for (var i = 1; i < array.Rank; i++) writer.Write(",");
                writer.Write("]");
            }
            return true;
        }

        public override bool VisitPointerType(IPointerTypeSymbol symbol, CodeWriter writer)
        {
            Visit(symbol.PointedAtType, writer);
            writer.Write("*");
            return true;
        }

        public override bool VisitNamespace(INamespaceSymbol symbol, CodeWriter writer)
        {
            if (symbol.IsGlobalNamespace)
            {
                writer.Write("global::");
                return true;
            }
            Visit(symbol.ContainingNamespace, writer);
            WriteIdentifier(writer, symbol.Name);
            writer.Write(".");
            return true;
        }

        public override bool VisitNamedType(INamedTypeSymbol symbol, CodeWriter writer)
        {
            if (SpecialTypeName(symbol) is { } keyword)
            {
                writer.Write(keyword);
                return true;
            }
            if (symbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                !symbol.IsDefinition && symbol.TypeArguments[0] is not IPointerTypeSymbol)
            {
                Visit(symbol.TypeArguments[0], writer);
                writer.Write("?");
                return true;
            }
            if (symbol.IsTupleType)
            {
                writer.Write("(");
                var elements = symbol.TupleElements;
                for (var i = 0; i < elements.Length; i++)
                {
                    if (i > 0) writer.Write(", ");
                    Visit(elements[i].Type, writer);
                    if (elements[i].IsExplicitlyNamedTupleElement)
                    {
                        writer.Write(" ");
                        WriteIdentifier(writer, elements[i].Name);
                    }
                }
                writer.Write(")");
                return true;
            }
            if (symbol.ContainingType is { } containing)
            {
                Visit(containing, writer);
                writer.Write(".");
            }
            else
            {
                Visit(symbol.ContainingNamespace, writer);
            }
            WriteIdentifier(writer, symbol.Name);
            if (symbol.Arity > 0)
            {
                writer.Write("<");
                if (symbol.IsUnboundGenericType)
                {
                    for (var i = 1; i < symbol.Arity; i++) writer.Write(",");
                }
                else
                {
                    for (var i = 0; i < symbol.TypeArguments.Length; i++)
                    {
                        if (i > 0) writer.Write(", ");
                        Visit(symbol.TypeArguments[i], writer);
                    }
                }
                writer.Write(">");
            }
            return true;
        }
    }

    private static string? SpecialTypeName(INamedTypeSymbol symbol) => symbol.SpecialType switch
    {
        SpecialType.System_Void => "void",
        SpecialType.System_Boolean => "bool",
        SpecialType.System_Char => "char",
        SpecialType.System_SByte => "sbyte",
        SpecialType.System_Byte => "byte",
        SpecialType.System_Int16 => "short",
        SpecialType.System_UInt16 => "ushort",
        SpecialType.System_Int32 => "int",
        SpecialType.System_UInt32 => "uint",
        SpecialType.System_Int64 => "long",
        SpecialType.System_UInt64 => "ulong",
        SpecialType.System_Decimal => "decimal",
        SpecialType.System_Single => "float",
        SpecialType.System_Double => "double",
        SpecialType.System_String => "string",
        SpecialType.System_Object => "object",
        SpecialType.System_IntPtr when symbol.IsNativeIntegerType => "nint",
        SpecialType.System_UIntPtr when symbol.IsNativeIntegerType => "nuint",
        _ => null,
    };
}
