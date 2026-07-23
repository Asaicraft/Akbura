using System.Globalization;

namespace Akbura.Diagnostics;

internal static class StateValueConverter
{
    public static bool CanEdit(Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;
        return valueType == typeof(string) ||
            valueType == typeof(bool) ||
            valueType == typeof(byte) ||
            valueType == typeof(sbyte) ||
            valueType == typeof(short) ||
            valueType == typeof(ushort) ||
            valueType == typeof(int) ||
            valueType == typeof(uint) ||
            valueType == typeof(long) ||
            valueType == typeof(ulong) ||
            valueType == typeof(float) ||
            valueType == typeof(double) ||
            valueType == typeof(decimal) ||
            valueType.IsEnum;
    }

    public static string FormatForEditor(object? value, Type type)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }

    public static bool TryParse(
        string? text,
        Type type,
        out object? value,
        out string error)
    {
        text ??= string.Empty;
        var valueType = Nullable.GetUnderlyingType(type);
        if (valueType != null && string.IsNullOrWhiteSpace(text))
        {
            value = null;
            error = string.Empty;
            return true;
        }

        valueType ??= type;
        if (valueType == typeof(string))
        {
            value = text;
            error = string.Empty;
            return true;
        }

        if (valueType.IsEnum)
        {
            if (Enum.TryParse(valueType, text, ignoreCase: true, out value))
            {
                error = string.Empty;
                return true;
            }

            error = $"Expected one of: {string.Join(", ", Enum.GetNames(valueType))}.";
            return false;
        }

        var style = NumberStyles.Integer;
        var culture = CultureInfo.InvariantCulture;
        var success = false;
        value = null;
        if (valueType == typeof(bool))
        {
            success = bool.TryParse(text, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(byte))
        {
            success = byte.TryParse(text, style, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(sbyte))
        {
            success = sbyte.TryParse(text, style, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(short))
        {
            success = short.TryParse(text, style, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(ushort))
        {
            success = ushort.TryParse(text, style, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(int))
        {
            success = int.TryParse(text, style, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(uint))
        {
            success = uint.TryParse(text, style, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(long))
        {
            success = long.TryParse(text, style, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(ulong))
        {
            success = ulong.TryParse(text, style, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(float))
        {
            success = float.TryParse(text, NumberStyles.Float, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(double))
        {
            success = double.TryParse(text, NumberStyles.Float, culture, out var parsed);
            value = parsed;
        }
        else if (valueType == typeof(decimal))
        {
            success = decimal.TryParse(text, NumberStyles.Number, culture, out var parsed);
            value = parsed;
        }

        if (success)
        {
            error = string.Empty;
            return true;
        }

        value = null;
        error = $"'{text}' is not a valid {valueType.Name} value.";
        return false;
    }
}
