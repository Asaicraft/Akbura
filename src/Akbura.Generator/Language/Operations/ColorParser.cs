using System;
using System.Globalization;
using System.Linq;

namespace Akbura.Language.Operations;

internal static class ColorParser
{
    public static bool TryParse(string? text, out AkcssColorValue color)
    {
        color = default;
        var normalizedText = (text ?? string.Empty).Trim();
        if (normalizedText.Length == 0)
        {
            return false;
        }

        if (normalizedText[0] == '#')
        {
            return TryParseHex(normalizedText.AsSpan(), out color);
        }

        if (normalizedText.Length >= 10 &&
            normalizedText.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) &&
            TryParseRgb(normalizedText, out color))
        {
            return true;
        }

        if (normalizedText.Length >= 10 &&
            normalizedText.StartsWith("hsl", StringComparison.OrdinalIgnoreCase) &&
            TryParseHsl(normalizedText, out color))
        {
            return true;
        }

        if (normalizedText.Length >= 10 &&
            normalizedText.StartsWith("hsv", StringComparison.OrdinalIgnoreCase) &&
            TryParseHsv(normalizedText, out color))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseHex(ReadOnlySpan<char> text, out AkcssColorValue color)
    {
        color = default;
        var input = text[1..].ToString();
        if (input.Length is 3 or 4)
        {
            var expanded = new char[input.Length * 2];
            for (var i = 0; i < input.Length; i++)
            {
                expanded[i * 2] = input[i];
                expanded[(i * 2) + 1] = input[i];
            }

            input = new string(expanded);
        }

        if (input.Length != 6 && input.Length != 8)
        {
            return false;
        }

        if (!uint.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (input.Length == 6)
        {
            value |= 0xFF000000;
        }

        color = new AkcssColorValue(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
        return true;
    }

    private static bool TryParseRgb(string text, out AkcssColorValue color)
    {
        color = default;
        if (!TryGetFunctionArguments(text, "rgb", "rgba", out var arguments))
        {
            return false;
        }

        var components = SplitComponents(arguments);
        if (components.Length is not (3 or 4) ||
            !TryParseByte(components[0], out var r) ||
            !TryParseByte(components[1], out var g) ||
            !TryParseByte(components[2], out var b))
        {
            return false;
        }

        var a = (byte)0xFF;
        if (components.Length == 4 &&
            !TryParseAlpha(components[3], out a))
        {
            return false;
        }

        color = new AkcssColorValue(a, r, g, b);
        return true;
    }

    private static bool TryParseHsl(string text, out AkcssColorValue color)
    {
        color = default;
        if (!TryGetFunctionArguments(text, "hsl", "hsla", out var arguments))
        {
            return false;
        }

        var components = SplitComponents(arguments);
        if (components.Length is not (3 or 4) ||
            !TryParseDouble(components[0], out var h) ||
            !TryParsePercent(components[1], out var s) ||
            !TryParsePercent(components[2], out var l))
        {
            return false;
        }

        var a = 1d;
        if (components.Length == 4 &&
            !TryParseAlphaDouble(components[3], out a))
        {
            return false;
        }

        HslToRgb(NormalizeHue(h), s, l, out var r, out var g, out var b);
        color = new AkcssColorValue(ToByte(a), ToByte(r), ToByte(g), ToByte(b));
        return true;
    }

    private static bool TryParseHsv(string text, out AkcssColorValue color)
    {
        color = default;
        if (!TryGetFunctionArguments(text, "hsv", "hsva", out var arguments))
        {
            return false;
        }

        var components = SplitComponents(arguments);
        if (components.Length is not (3 or 4) ||
            !TryParseDouble(components[0], out var h) ||
            !TryParsePercent(components[1], out var s) ||
            !TryParsePercent(components[2], out var v))
        {
            return false;
        }

        var a = 1d;
        if (components.Length == 4 &&
            !TryParseAlphaDouble(components[3], out a))
        {
            return false;
        }

        HsvToRgb(NormalizeHue(h), s, v, out var r, out var g, out var b);
        color = new AkcssColorValue(ToByte(a), ToByte(r), ToByte(g), ToByte(b));
        return true;
    }

    private static bool TryGetFunctionArguments(
        string text,
        string rgbName,
        string rgbaName,
        out string arguments)
    {
        arguments = string.Empty;
        var openParen = text.IndexOf('(');
        if (openParen < 0 ||
            !text.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var name = text[..openParen].Trim();
        if (!string.Equals(name, rgbName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, rgbaName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        arguments = text.Substring(openParen + 1, text.Length - openParen - 2);
        return true;
    }

    private static string[] SplitComponents(string arguments)
    {
        return arguments.Split(',')
            .Select(static component => component.Trim())
            .ToArray();
    }

    private static bool TryParseByte(string text, out byte value)
    {
        value = 0;
        if (text.EndsWith("%", StringComparison.Ordinal))
        {
            if (!TryParseDouble(text[..^1], out var percent))
            {
                return false;
            }

            value = ToByte(percent / 100d);
            return true;
        }

        return byte.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseAlpha(string text, out byte value)
    {
        value = 0xFF;
        if (!TryParseAlphaDouble(text, out var alpha))
        {
            return false;
        }

        value = ToByte(alpha);
        return true;
    }

    private static bool TryParseAlphaDouble(string text, out double value)
    {
        value = 1d;
        if (text.EndsWith("%", StringComparison.Ordinal))
        {
            return TryParseDouble(text[..^1], out value) && (value /= 100d) >= 0d && value <= 1d;
        }

        return TryParseDouble(text, out value) && value >= 0d && value <= 1d;
    }

    private static bool TryParsePercent(string text, out double value)
    {
        value = 0d;
        if (!text.EndsWith("%", StringComparison.Ordinal) ||
            !TryParseDouble(text[..^1], out value))
        {
            return false;
        }

        value /= 100d;
        return value >= 0d && value <= 1d;
    }

    private static bool TryParseDouble(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static double NormalizeHue(double hue)
    {
        hue %= 360d;
        return hue < 0d ? hue + 360d : hue;
    }

    private static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
    {
        var c = (1d - Math.Abs((2d * l) - 1d)) * s;
        var x = c * (1d - Math.Abs(((h / 60d) % 2d) - 1d));
        var m = l - (c / 2d);
        ToRgbByHue(h, c, x, m, out r, out g, out b);
    }

    private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        var c = v * s;
        var x = c * (1d - Math.Abs(((h / 60d) % 2d) - 1d));
        var m = v - c;
        ToRgbByHue(h, c, x, m, out r, out g, out b);
    }

    private static void ToRgbByHue(double h, double c, double x, double m, out double r, out double g, out double b)
    {
        (r, g, b) = h switch
        {
            < 60d => (c, x, 0d),
            < 120d => (x, c, 0d),
            < 180d => (0d, c, x),
            < 240d => (0d, x, c),
            < 300d => (x, 0d, c),
            _ => (c, 0d, x),
        };

        r += m;
        g += m;
        b += m;
    }

    private static byte ToByte(double normalized)
    {
        return (byte)Math.Round(Clamp01(normalized) * 255d);
    }

    private static double Clamp01(double value)
    {
        if (value < 0d)
        {
            return 0d;
        }

        return value > 1d ? 1d : value;
    }
}
