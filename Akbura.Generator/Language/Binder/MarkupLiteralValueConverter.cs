using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Globalization;
using System.Linq;

namespace Akbura.Language.Binder;

internal enum MarkupLiteralConversionStatus : byte
{
    Unsupported,
    Success,
    Invalid,
}

internal static class MarkupLiteralValueConverter
{
    public static MarkupLiteralConversionStatus Convert(
        string text,
        ITypeSymbol targetType,
        CSharpCompilation compilation,
        out object? value)
    {
        value = null;
        var effectiveType = GetEffectiveType(targetType);
        if (effectiveType.SpecialType is SpecialType.System_String or SpecialType.System_Object)
        {
            value = text;
            return MarkupLiteralConversionStatus.Success;
        }

        var primitiveStatus = TryConvertPrimitive(text, effectiveType.SpecialType, out value);
        if (primitiveStatus != MarkupLiteralConversionStatus.Unsupported)
        {
            return primitiveStatus;
        }

        if (effectiveType.TypeKind == TypeKind.Enum)
        {
            var member = effectiveType.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(field =>
                    field.IsStatic &&
                    field.DeclaredAccessibility == Accessibility.Public &&
                    string.Equals(field.Name, text, StringComparison.OrdinalIgnoreCase));
            if (member == null)
            {
                return MarkupLiteralConversionStatus.Invalid;
            }

            value = new CSharpSymbolDefinition(member);
            return MarkupLiteralConversionStatus.Success;
        }

        if (effectiveType is not INamedTypeSymbol namedType)
        {
            return MarkupLiteralConversionStatus.Unsupported;
        }

        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var parseMethod = namedType.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsStatic &&
                method.DeclaredAccessibility == Accessibility.Public &&
                method.Arity == 0 &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, stringType) &&
                compilation.ClassifyConversion(method.ReturnType, targetType).IsImplicit);
        if (parseMethod != null)
        {
            value = new MarkupLiteralValue(
                text,
                new CSharpSymbolDefinition(targetType),
                MarkupLiteralConverterKind.ParseMethod,
                new CSharpSymbolDefinition(parseMethod));
            return MarkupLiteralConversionStatus.Success;
        }

        var constructor = namedType.InstanceConstructors.FirstOrDefault(method =>
            method.DeclaredAccessibility == Accessibility.Public &&
            method.Parameters.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, stringType));
        if (constructor != null)
        {
            value = new MarkupLiteralValue(
                text,
                new CSharpSymbolDefinition(targetType),
                MarkupLiteralConverterKind.StringConstructor,
                new CSharpSymbolDefinition(constructor));
            return MarkupLiteralConversionStatus.Success;
        }

        return MarkupLiteralConversionStatus.Unsupported;
    }

    private static ITypeSymbol GetEffectiveType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments.Length: 1,
        } nullableType
            ? nullableType.TypeArguments[0]
            : type;
    }

    private static MarkupLiteralConversionStatus TryConvertPrimitive(
        string text,
        SpecialType specialType,
        out object? value)
    {
        value = null;
        var culture = CultureInfo.InvariantCulture;
        switch (specialType)
        {
            case SpecialType.System_Boolean:
                return SetResult(bool.TryParse(text, out var boolean), boolean, out value);
            case SpecialType.System_Char:
                return SetResult(TryParseChar(text, out var character), character, out value);
            case SpecialType.System_SByte:
                return SetResult(sbyte.TryParse(text, NumberStyles.Integer, culture, out var signedByte), signedByte, out value);
            case SpecialType.System_Byte:
                return SetResult(byte.TryParse(text, NumberStyles.Integer, culture, out var unsignedByte), unsignedByte, out value);
            case SpecialType.System_Int16:
                return SetResult(short.TryParse(text, NumberStyles.Integer, culture, out var int16), int16, out value);
            case SpecialType.System_UInt16:
                return SetResult(ushort.TryParse(text, NumberStyles.Integer, culture, out var uint16), uint16, out value);
            case SpecialType.System_Int32:
                return SetResult(int.TryParse(text, NumberStyles.Integer, culture, out var int32), int32, out value);
            case SpecialType.System_UInt32:
                return SetResult(uint.TryParse(text, NumberStyles.Integer, culture, out var uint32), uint32, out value);
            case SpecialType.System_Int64:
                return SetResult(long.TryParse(text, NumberStyles.Integer, culture, out var int64), int64, out value);
            case SpecialType.System_UInt64:
                return SetResult(ulong.TryParse(text, NumberStyles.Integer, culture, out var uint64), uint64, out value);
            case SpecialType.System_Single:
                return SetResult(float.TryParse(text, NumberStyles.Float, culture, out var single), single, out value);
            case SpecialType.System_Double:
                return SetResult(double.TryParse(text, NumberStyles.Float, culture, out var @double), @double, out value);
            case SpecialType.System_Decimal:
                return SetResult(decimal.TryParse(text, NumberStyles.Number, culture, out var @decimal), @decimal, out value);
            default:
                return MarkupLiteralConversionStatus.Unsupported;
        }
    }

    private static MarkupLiteralConversionStatus SetResult<T>(
        bool success,
        T parsedValue,
        out object? value)
    {
        value = success ? parsedValue : null;
        return success
            ? MarkupLiteralConversionStatus.Success
            : MarkupLiteralConversionStatus.Invalid;
    }

    private static bool TryParseChar(string text, out char value)
    {
        if (text.Length == 1)
        {
            value = text[0];
            return true;
        }

        value = default;
        return false;
    }
}
