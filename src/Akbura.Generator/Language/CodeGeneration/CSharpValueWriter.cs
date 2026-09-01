using CSharpSymbolDefinition = Akbura.Language.Symbols.CSharpSymbolDefinition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Diagnostics;
using System.Globalization;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes C# identifiers, type names, member references, and constant values
/// directly to a CodeWriter.
/// </summary>
internal readonly ref struct CSharpValueWriter
{
    private static readonly SymbolDisplayFormat s_typeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly CodeWriter _writer;

    public CSharpValueWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
    }

    public void WriteIdentifier(string identifier)
    {
        _writer.WriteIdentifierEscapeIfNeeded(identifier);
        _writer.Write(identifier);
    }

    public void WriteTypeName(ISymbol? symbol)
    {
        WriteTypeName(symbol as ITypeSymbol);
    }

    public void WriteTypeName(ITypeSymbol? type)
    {
        if (type == null || ContainsErrorType(type))
        {
            _writer.Write("global::System.Object");
            return;
        }

        // Roslyn does not expose a streaming SymbolDisplay API, so this
        // currently creates one temporary string.
        _writer.Write(type.ToDisplayString(s_typeDisplayFormat));
    }

    public void WriteStaticMemberReference(ISymbol symbol)
    {
        Debug.Assert(symbol is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true });

        WriteTypeName(symbol.ContainingType);
        _writer.Write(".");
        WriteIdentifier(symbol.Name);
    }

    public bool TryWriteStaticMemberReference(ISymbol? symbol)
    {
        if (symbol is not IFieldSymbol { IsStatic: true } and not IPropertySymbol { IsStatic: true })
        {
            return false;
        }

        WriteStaticMemberReference(symbol);
        return true;
    }

    public void WriteConstant(object? value, ISymbol? targetType)
    {
        if (value == null)
        {
            _writer.Write("null");
            return;
        }

        var enumType = GetEnumType(targetType);

        if (enumType != null && value is ulong unsignedEnumValue && unsignedEnumValue > long.MaxValue)
        {
            _writer.Write("unchecked((");
            WriteTypeName(enumType);
            _writer.Write(")");
            WriteUInt64(unsignedEnumValue);
            _writer.Write(")");
            return;
        }

        if (enumType != null && TryConvertToInt64(value, out var enumValue))
        {
            _writer.Write("(");
            WriteTypeName(enumType);
            _writer.Write(")");

            if (enumValue < 0)
            {
                _writer.Write("(");
                WriteInt64(enumValue);
                _writer.Write(")");
            }
            else
            {
                WriteInt64(enumValue);
            }

            return;
        }

        switch (value)
        {
            case CSharpSymbolDefinition definition when TryWriteStaticMemberReference(definition.Symbol):
                return;

            case ITypeSymbol type:
                _writer.Write("typeof(");
                WriteTypeName(type);
                _writer.Write(")");
                return;

            case string text:
                _writer.WriteStringLiteral(text);
                return;

            case char character:
                WriteCharacterLiteral(character);
                return;

            case bool boolean:
                _writer.WriteBooleanLiteral(boolean);
                return;

            case byte number:
                _writer.WriteIntegerLiteral(number);
                return;

            case sbyte number:
                _writer.WriteIntegerLiteral(number);
                return;

            case short number:
                _writer.WriteIntegerLiteral(number);
                return;

            case ushort number:
                _writer.WriteIntegerLiteral(number);
                return;

            case int number:
                _writer.WriteIntegerLiteral(number);
                return;

            case uint number:
                WriteUInt32(number);
                return;

            case long number:
                WriteInt64(number);
                return;

            case ulong number:
                WriteUInt64(number);
                return;

            case float number:
                WriteSingle(number);
                return;

            case double number:
                WriteDouble(number);
                return;

            case decimal number:
                WriteDecimal(number);
                return;

            default:
                Debug.Fail("Unsupported constant value: " + value.GetType().FullName);
                _writer.WriteStringLiteral(value.ToString() ?? string.Empty);
                return;
        }
    }

    private void WriteUInt32(uint value)
    {
        if (value <= int.MaxValue)
        {
            _writer.WriteIntegerLiteral((int)value);
        }
        else
        {
            _writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        _writer.Write("u");
    }

    private void WriteInt64(long value)
    {
        if (value is >= int.MinValue and <= int.MaxValue)
        {
            _writer.WriteIntegerLiteral((int)value);
        }
        else
        {
            _writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        _writer.Write("L");
    }

    private void WriteUInt64(ulong value)
    {
        if (value <= int.MaxValue)
        {
            _writer.WriteIntegerLiteral((int)value);
        }
        else
        {
            _writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        _writer.Write("UL");
    }

    private void WriteSingle(float value)
    {
        if (float.IsNaN(value))
        {
            _writer.Write("global::System.Single.NaN");
            return;
        }

        if (float.IsPositiveInfinity(value))
        {
            _writer.Write("global::System.Single.PositiveInfinity");
            return;
        }

        if (float.IsNegativeInfinity(value))
        {
            _writer.Write("global::System.Single.NegativeInfinity");
            return;
        }

        _writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
        _writer.Write("f");
    }

    private void WriteDouble(double value)
    {
        if (double.IsNaN(value))
        {
            _writer.Write("global::System.Double.NaN");
            return;
        }

        if (double.IsPositiveInfinity(value))
        {
            _writer.Write("global::System.Double.PositiveInfinity");
            return;
        }

        if (double.IsNegativeInfinity(value))
        {
            _writer.Write("global::System.Double.NegativeInfinity");
            return;
        }

        _writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
        _writer.Write("d");
    }

    private void WriteDecimal(decimal value)
    {
        _writer.Write(value.ToString(CultureInfo.InvariantCulture));
        _writer.Write("m");
    }

    private void WriteCharacterLiteral(char value)
    {
        switch (value)
        {
            case '\0':
                _writer.Write("'\\0'");
                return;

            case '\a':
                _writer.Write("'\\a'");
                return;

            case '\b':
                _writer.Write("'\\b'");
                return;

            case '\f':
                _writer.Write("'\\f'");
                return;

            case '\n':
                _writer.Write("'\\n'");
                return;

            case '\r':
                _writer.Write("'\\r'");
                return;

            case '\t':
                _writer.Write("'\\t'");
                return;

            case '\v':
                _writer.Write("'\\v'");
                return;

            case '\'':
                _writer.Write("'\\\''");
                return;

            case '\\':
                _writer.Write("'\\\\'");
                return;
        }

        // SymbolDisplay handles control characters, surrogates, and other
        // uncommon cases correctly.
        _writer.Write(SymbolDisplay.FormatLiteral(value, quote: true));
    }

    private static bool TryConvertToInt64(object value, out long result)
    {
        switch (value)
        {
            case byte number:
                result = number;
                return true;

            case sbyte number:
                result = number;
                return true;

            case short number:
                result = number;
                return true;

            case ushort number:
                result = number;
                return true;

            case int number:
                result = number;
                return true;

            case uint number:
                result = number;
                return true;

            case long number:
                result = number;
                return true;

            case ulong number when number <= long.MaxValue:
                result = (long)number;
                return true;

            default:
                result = 0;
                return false;
        }
    }

    private static INamedTypeSymbol? GetEnumType(ISymbol? targetType)
    {
        if (targetType is not INamedTypeSymbol namedType)
        {
            return null;
        }

        if (namedType.TypeKind == TypeKind.Enum)
        {
            return namedType;
        }

        if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Enum } nullableEnumType)
        {
            return nullableEnumType;
        }

        return null;
    }

    private static bool ContainsErrorType(ITypeSymbol type)
    {
        if (type is IErrorTypeSymbol || type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                return ContainsErrorType(array.ElementType);

            case IPointerTypeSymbol pointer:
                return ContainsErrorType(pointer.PointedAtType);

            case INamedTypeSymbol named:
                if (named.ContainingType != null && ContainsErrorType(named.ContainingType))
                {
                    return true;
                }

                for (var i = 0; i < named.TypeArguments.Length; i++)
                {
                    if (ContainsErrorType(named.TypeArguments[i]))
                    {
                        return true;
                    }
                }

                return false;

            case IFunctionPointerTypeSymbol functionPointer:
                if (ContainsErrorType(functionPointer.Signature.ReturnType))
                {
                    return true;
                }

                var parameters = functionPointer.Signature.Parameters;

                for (var i = 0; i < parameters.Length; i++)
                {
                    if (ContainsErrorType(parameters[i].Type))
                    {
                        return true;
                    }
                }

                var callingConventionTypes = functionPointer.Signature.UnmanagedCallingConventionTypes;

                for (var i = 0; i < callingConventionTypes.Length; i++)
                {
                    if (ContainsErrorType(callingConventionTypes[i]))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }
}
