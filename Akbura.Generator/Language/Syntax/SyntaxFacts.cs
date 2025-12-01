using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Akbura.Language.Syntax;
/// <summary>Provides helper methods for working with syntax kinds.</summary>
public static partial class SyntaxFacts
{

    /// <summary>Gets the textual representation of a given syntax kind.</summary>
    /// <param name="kind">The syntax kind.</param>
    /// <return>The text representation of the syntax kind.</return>
    public static partial string GetText(SyntaxKind kind);

    public static partial bool IsAnyToken(SyntaxKind kind);

    public static partial bool IsTrivia(SyntaxKind kind);

    public static partial bool IsLiteral(SyntaxKind kind);

    public static partial SyntaxKind GetKeywordKind(string text);
    public static partial SyntaxKind GetContextualKeywordKind(string text);

    public static partial bool IsContextualKeyword(SyntaxKind kind);

    public static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') ||
               (c >= 'A' && c <= 'F') ||
               (c >= 'a' && c <= 'f');
    }

    public static bool IsBinaryDigit(char c)
    {
        return c == '0' | c == '1';
    }

    public static bool IsDecDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    public static int HexValue(char c)
    {
        Debug.Assert(IsHexDigit(c));
        return (c >= '0' && c <= '9') ? c - '0' : (c & 0xdf) - 'A' + 10;
    }

    public static int BinaryValue(char c)
    {
        Debug.Assert(IsBinaryDigit(c));
        return c - '0';
    }

    public static int DecValue(char c)
    {
        Debug.Assert(IsDecDigit(c));
        return c - '0';
    }

    public static bool IsWhitespace(char ch)
    {
        return ch == ' '
            || ch == '\t'
            || ch == '\v'
            || ch == '\f'
            || ch == '\u00A0' // NO-BREAK SPACE
            || ch == '\uFEFF'
            || ch == '\u001A'
            || (ch > 255 && CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.SpaceSeparator);
    }

    public static bool IsNewLine(char ch)
    {
        return ch == '\r'
            || ch == '\n'
            || ch == '\u0085'
            || ch == '\u2028'
            || ch == '\u2029';
    }

    public static bool IsIdentifierStartCharacter(char ch)
    {
        return UnicodeCharacterUtilities.IsIdentifierStartCharacter(ch);
    }

    public static bool IsIdentifierPartCharacter(char ch)
    {
        return UnicodeCharacterUtilities.IsIdentifierPartCharacter(ch);
    }

    public static bool IsValidIdentifier(string? name)
    {
        return UnicodeCharacterUtilities.IsValidIdentifier(name);
    }

    public static bool ContainsDroppedIdentifierCharacters(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        if (name![0] == '@')
        {
            return true;
        }
        var nameLength = name.Length;
        for (var i = 0; i < nameLength; i++)
        {
            if (UnicodeCharacterUtilities.IsFormattingChar(name[i]))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsNonAsciiQuotationMark(char ch)
    {
        return ch switch
        {
            '‘' or '’' => true,
            '“' or '”' => true,
            _ => false,
        };
    }

    public static partial object? GetValue(SyntaxKind kind, string text);
}