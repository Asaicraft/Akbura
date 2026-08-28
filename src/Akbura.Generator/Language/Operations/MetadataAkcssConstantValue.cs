using Microsoft.CodeAnalysis;
using System;
using System.Globalization;

namespace Akbura.Language.Operations;

internal static class MetadataAkcssConstantValue
{
    public static object? Parse(string? text, ITypeSymbol? type)
    {
        if (text == null || type == null)
        {
            return null;
        }

        return type.SpecialType switch
        {
            SpecialType.System_String => text,
            SpecialType.System_Char when text.Length == 1 => text[0],
            SpecialType.System_Boolean when bool.TryParse(text, out var value) => value,
            SpecialType.System_Byte when byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_SByte when sbyte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Int16 when short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_UInt16 when ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Int32 when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_UInt32 when uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Int64 when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_UInt64 when ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Single when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Double when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Decimal when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
            _ => text,
        };
    }
}
