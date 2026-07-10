using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Akbura.Language;

internal enum GridDefinitionUnitType
{
    Auto,
    Pixel,
    Star,
}

internal readonly struct GridDefinitionLengthValue : IEquatable<GridDefinitionLengthValue>
{
    public GridDefinitionLengthValue(double value, GridDefinitionUnitType unitType)
    {
        Value = value;
        UnitType = unitType;
    }

    public double Value { get; }

    public GridDefinitionUnitType UnitType { get; }

    public static GridDefinitionLengthValue Auto => new(0, GridDefinitionUnitType.Auto);

    public static GridDefinitionLengthValue Star => new(1, GridDefinitionUnitType.Star);

    public bool Equals(GridDefinitionLengthValue other)
    {
        return Value.Equals(other.Value) && UnitType == other.UnitType;
    }

    public override bool Equals(object? obj)
    {
        return obj is GridDefinitionLengthValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Value, UnitType);
    }
}

internal readonly struct GridDefinitionValue : IEquatable<GridDefinitionValue>
{
    public GridDefinitionValue(
        GridDefinitionLengthValue length,
        double? min = null,
        double? max = null)
    {
        Length = length;
        Min = min;
        Max = max;
    }

    public GridDefinitionLengthValue Length { get; }

    public double? Min { get; }

    public double? Max { get; }

    public bool Equals(GridDefinitionValue other)
    {
        return Length.Equals(other.Length) &&
               Nullable.Equals(Min, other.Min) &&
               Nullable.Equals(Max, other.Max);
    }

    public override bool Equals(object? obj)
    {
        return obj is GridDefinitionValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Length, Min, Max);
    }
}

internal readonly struct GridDefinitionListValue : IEquatable<GridDefinitionListValue>
{
    public GridDefinitionListValue(ImmutableArray<GridDefinitionValue> definitions)
    {
        Definitions = definitions.IsDefault
            ? ImmutableArray<GridDefinitionValue>.Empty
            : definitions;
    }

    public ImmutableArray<GridDefinitionValue> Definitions { get; }

    public bool Equals(GridDefinitionListValue other)
    {
        return Definitions.SequenceEqual(other.Definitions);
    }

    public override bool Equals(object? obj)
    {
        return obj is GridDefinitionListValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var definition in Definitions)
        {
            hash = HashCode.Combine(hash, definition);
        }

        return hash;
    }
}

internal static class GridDefinitionLiteralParser
{
    public static bool TryParse(string? text)
    {
        return TryParse(text, out _);
    }

    public static bool TryParse(
        string? text,
        out GridDefinitionListValue value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = new GridDefinitionListValue(ImmutableArray<GridDefinitionValue>.Empty);
            return true;
        }

        var source = text!;
        if (!TrySplitTopLevelItems(source, splitOnWhitespace: !HasTopLevelComma(source), out var items))
        {
            value = default;
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<GridDefinitionValue>(items.Count);
        foreach (var item in items)
        {
            if (!TryParseDefinition(item, out var definition))
            {
                value = default;
                return false;
            }

            builder.Add(definition);
        }

        value = new GridDefinitionListValue(builder.ToImmutable());
        return true;
    }

    private static bool TryParseDefinition(
        string text,
        out GridDefinitionValue definition)
    {
        definition = default;
        text = text.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (TryParseGridLength(text, out var length))
        {
            definition = new GridDefinitionValue(length);
            return true;
        }

        if (TryGetCallArguments(text, "min", out var minArguments))
        {
            if (minArguments.Count != 1 ||
                !TryParsePixelLength(minArguments[0], out var min))
            {
                return false;
            }

            definition = new GridDefinitionValue(GridDefinitionLengthValue.Star, min: min);
            return true;
        }

        if (TryGetCallArguments(text, "max", out var maxArguments))
        {
            if (maxArguments.Count != 1 ||
                !TryParsePixelLength(maxArguments[0], out var max))
            {
                return false;
            }

            definition = new GridDefinitionValue(GridDefinitionLengthValue.Star, max: max);
            return true;
        }

        if (!TryGetCallArguments(text, "min-max", out var minMaxArguments))
        {
            return false;
        }

        if (minMaxArguments.Count == 2)
        {
            if (!TryParsePixelLength(minMaxArguments[0], out var min) ||
                !TryParsePixelLength(minMaxArguments[1], out var max) ||
                min > max)
            {
                return false;
            }

            definition = new GridDefinitionValue(
                GridDefinitionLengthValue.Star,
                min,
                max);
            return true;
        }

        if (minMaxArguments.Count != 3 ||
            !TryParsePixelLength(minMaxArguments[0], out var minValue) ||
            !TryParseGridLength(minMaxArguments[1], out var minMaxLength) ||
            !TryParsePixelLength(minMaxArguments[2], out var maxValue) ||
            minValue > maxValue)
        {
            return false;
        }

        definition = new GridDefinitionValue(
            minMaxLength,
            minValue,
            maxValue);
        return true;
    }

    private static bool TryGetCallArguments(
        string text,
        string name,
        out List<string> arguments)
    {
        arguments = [];
        if (!text.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var openIndex = name.Length;
        while (openIndex < text.Length && char.IsWhiteSpace(text[openIndex]))
        {
            openIndex++;
        }

        if (openIndex >= text.Length ||
            text[openIndex] != '(' ||
            text[^1] != ')' ||
            !HasBalancedOuterParentheses(text, openIndex))
        {
            return false;
        }

        var argumentText = text.Substring(openIndex + 1, text.Length - openIndex - 2);
        return TrySplitTopLevelItems(argumentText, splitOnWhitespace: false, out arguments) &&
               arguments.Count > 0;
    }

    private static bool TryParseGridLength(
        string text,
        out GridDefinitionLengthValue length)
    {
        length = default;
        text = text.Trim();
        if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            length = GridDefinitionLengthValue.Auto;
            return true;
        }

        if (text.EndsWith("*", StringComparison.Ordinal))
        {
            var valueText = text.Substring(0, text.Length - 1).Trim();
            if (valueText.Length == 0)
            {
                length = GridDefinitionLengthValue.Star;
                return true;
            }

            if (!TryParsePixelLength(valueText, out var starValue))
            {
                return false;
            }

            length = new GridDefinitionLengthValue(starValue, GridDefinitionUnitType.Star);
            return true;
        }

        if (!TryParsePixelLength(text, out var pixelValue))
        {
            return false;
        }

        length = new GridDefinitionLengthValue(pixelValue, GridDefinitionUnitType.Pixel);
        return true;
    }

    private static bool TryParsePixelLength(string text, out double value)
    {
        return double.TryParse(
                   text.Trim(),
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value >= 0 &&
               !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }

    private static bool HasTopLevelComma(string text)
    {
        var depth = 0;
        foreach (var character in text)
        {
            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return false;
                }
            }
            else if (character == ',' && depth == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySplitTopLevelItems(
        string text,
        bool splitOnWhitespace,
        out List<string> items)
    {
        items = [];
        var depth = 0;
        var start = 0;
        var lastSeparatorWasComma = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return false;
                }

                continue;
            }

            if (depth != 0 ||
                character != ',' && !(splitOnWhitespace && char.IsWhiteSpace(character)))
            {
                continue;
            }

            var item = text.Substring(start, index - start).Trim();
            if (item.Length == 0)
            {
                if (character == ',')
                {
                    return false;
                }

                start = index + 1;
                continue;
            }

            items.Add(item);
            start = index + 1;
            lastSeparatorWasComma = character == ',';
        }

        if (depth != 0)
        {
            return false;
        }

        var finalItem = text.Substring(start).Trim();
        if (finalItem.Length == 0)
        {
            return !lastSeparatorWasComma &&
                   (items.Count > 0 || string.IsNullOrWhiteSpace(text));
        }

        items.Add(finalItem);
        return true;
    }

    private static bool HasBalancedOuterParentheses(string text, int openIndex)
    {
        var depth = 0;
        for (var index = openIndex; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return index == text.Length - 1;
                }
            }
        }

        return false;
    }
}
