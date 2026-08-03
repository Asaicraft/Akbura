using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Akbura.Diagnostics;

internal static class StateValueConverter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static bool CanEdit(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type != typeof(void) &&
            !type.IsByRef &&
            !type.IsPointer &&
            !type.IsByRefLike &&
            !type.ContainsGenericParameters;
    }

    public static bool ShouldUseJson(Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;
        if (valueType == typeof(string) ||
            valueType.IsEnum ||
            valueType.IsPrimitive ||
            valueType == typeof(decimal))
        {
            return false;
        }

        var converter = TypeDescriptor.GetConverter(valueType);
        return !converter.CanConvertFrom(typeof(string));
    }

    public static string FormatForEditor(object? value, Type type)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        var valueType = Nullable.GetUnderlyingType(type) ?? type;
        var converter = TypeDescriptor.GetConverter(valueType);
        if (converter.CanConvertFrom(typeof(string)) &&
            converter.CanConvertTo(typeof(string)))
        {
            try
            {
                return converter.ConvertToInvariantString(value) ?? string.Empty;
            }
            catch (Exception exception) when (IsConversionException(exception))
            {
            }
        }

        if (value is IFormattable formattable && !ShouldUseJson(valueType))
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture)
                ?? string.Empty;
        }

        try
        {
            var serializationType = valueType == typeof(object) ||
                valueType.IsAbstract ||
                valueType.IsInterface
                    ? value.GetType()
                    : valueType;

            return JsonSerializer.Serialize(
                value,
                serializationType,
                s_jsonOptions);
        }
        catch (Exception exception) when (IsConversionException(exception))
        {
            return DebugString.Format(value);
        }
    }

    public static bool TryParse(
        string? text,
        Type type,
        out object? value,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(type);

        text ??= string.Empty;
        var nullableType = Nullable.GetUnderlyingType(type);
        var valueType = nullableType ?? type;

        if (valueType == typeof(string))
        {
            value = text;
            error = string.Empty;
            return true;
        }

        if ((nullableType is not null || !type.IsValueType) &&
            (string.IsNullOrWhiteSpace(text) ||
             string.Equals(text.Trim(), "null", StringComparison.OrdinalIgnoreCase)))
        {
            value = null;
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

        if (TryConvertFromString(text, valueType, out value) ||
            TryInvokeParse(text, valueType, out value) ||
            TryDeserializeJson(text, valueType, out value))
        {
            error = string.Empty;
            return true;
        }

        value = null;
        error = $"'{text}' is not a valid {valueType.Name} value. " +
            "Enter a value accepted by its string converter or a JSON representation.";
        return false;
    }

    private static bool TryConvertFromString(
        string text,
        Type valueType,
        out object? value)
    {
        var converter = TypeDescriptor.GetConverter(valueType);
        if (converter.CanConvertFrom(typeof(string)))
        {
            try
            {
                value = converter.ConvertFromInvariantString(text);
                return value is not null || !valueType.IsValueType;
            }
            catch (Exception exception) when (IsConversionException(exception))
            {
            }
        }

        value = null;
        return false;
    }

    private static bool TryInvokeParse(
        string text,
        Type valueType,
        out object? value)
    {
        foreach (var method in valueType.GetMethods(
                     BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != "TryParse" || method.ReturnType != typeof(bool))
            {
                continue;
            }

            var parameters = method.GetParameters();
            object?[]? arguments = parameters switch
            {
                [{ ParameterType: var textType }, { IsOut: true } output]
                    when textType == typeof(string) &&
                         output.ParameterType == valueType.MakeByRefType() =>
                    [text, null],

                [{ ParameterType: var textType }, { ParameterType: var providerType }, { IsOut: true } output]
                    when textType == typeof(string) &&
                         providerType == typeof(IFormatProvider) &&
                         output.ParameterType == valueType.MakeByRefType() =>
                    [text, CultureInfo.InvariantCulture, null],

                _ => null,
            };

            if (arguments is null)
            {
                continue;
            }

            try
            {
                if (method.Invoke(null, arguments) is true)
                {
                    value = arguments[^1];
                    return true;
                }
            }
            catch (Exception exception) when (IsConversionException(exception))
            {
            }
        }

        foreach (var method in valueType.GetMethods(
                     BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != "Parse" || method.ReturnType != valueType)
            {
                continue;
            }

            var parameters = method.GetParameters();
            object?[]? arguments = parameters switch
            {
                [{ ParameterType: var textType }]
                    when textType == typeof(string) =>
                    [text],

                [{ ParameterType: var textType }, { ParameterType: var providerType }]
                    when textType == typeof(string) &&
                         providerType == typeof(IFormatProvider) =>
                    [text, CultureInfo.InvariantCulture],

                _ => null,
            };

            if (arguments is null)
            {
                continue;
            }

            try
            {
                value = method.Invoke(null, arguments);
                return value is not null;
            }
            catch (Exception exception) when (IsConversionException(exception))
            {
            }
        }

        value = null;
        return false;
    }

    private static bool TryDeserializeJson(
        string text,
        Type valueType,
        out object? value)
    {
        if (valueType == typeof(object))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                value = ConvertJsonElement(document.RootElement);
                return true;
            }
            catch (JsonException)
            {
                value = text;
                return true;
            }
        }

        try
        {
            value = JsonSerializer.Deserialize(
                text,
                valueType,
                s_jsonOptions);
            return value is not null || !valueType.IsValueType;
        }
        catch (Exception exception) when (IsConversionException(exception))
        {
            value = null;
            return false;
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList(),
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(
                    static property => property.Name,
                    static property => ConvertJsonElement(property.Value)),
            _ => element.GetRawText(),
        };
    }

    private static bool IsConversionException(Exception exception)
    {
        return exception is ArgumentException
            or FormatException
            or InvalidCastException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException
            or TargetInvocationException
            or JsonException;
    }
}
