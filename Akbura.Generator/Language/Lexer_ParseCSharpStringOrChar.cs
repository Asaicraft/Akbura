using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Text;
using CodeAnalysisToken = Microsoft.CodeAnalysis.SyntaxToken;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CodeAnalysisSyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;
using Microsoft.CodeAnalysis.CSharp;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

#pragma warning disable RSEXPERIMENTAL003 // SyntaxTokenParser is used as a targeted C# raw-token helper.

partial class Lexer
{
    private bool TryScanCSharpStringOrCharText([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        text = null;

        if (!ScanCSharpStringOrChar())
        {
            return false;
        }

        if (IsCSharpInterpolatedStringStart())
        {
            var start = TextWindow.Position;
            var expression = CSharpSyntaxFactory.ParseExpression(
                TextWindow.Text.ToString(),
                start,
                options: null,
                consumeFullText: false);

            if (expression is not CSharp.InterpolatedStringExpressionSyntax ||
                expression.FullSpan.Length <= 0)
            {
                return false;
            }

            text = TextWindow.GetText(start, expression.FullSpan.Length, intern: false);
            TextWindow.Reset(start + expression.FullSpan.Length);
            return true;
        }

        var token = ParseCSharpStringOrChar();
        if (token.RawKind == 0)
        {
            return false;
        }

        text = token.Text;
        return true;
    }

    private bool IsCSharpInterpolatedStringStart()
    {
        var ch = TextWindow.PeekChar();
        if (ch == '@' && TextWindow.PeekChar(1) == '$')
        {
            return TextWindow.PeekChar(2) == '"';
        }

        if (ch != '$')
        {
            return false;
        }

        var offset = 1;
        while (TextWindow.PeekChar(offset) == '$')
        {
            offset++;
        }

        if (TextWindow.PeekChar(offset) == '@')
        {
            offset++;
        }

        return TextWindow.PeekChar(offset) == '"';
    }

    private bool ScanCSharpStringOrChar()
    {
        var ch = TextWindow.PeekChar();

        // Character literal: 'a'
        if (ch == '\'')
        {
            return true;
        }

        // Simple string literal: "text"
        if (ch == '"')
        {
            return true;
        }

        // Raw string literal: """text"""
        if (ch == '"' &&
            TextWindow.PeekChar(1) == '"' &&
            TextWindow.PeekChar(2) == '"')
        {
            return true;
        }

        // Interpolated string literal: $"text"
        if (ch == '$')
        {
            var n1 = TextWindow.PeekChar(1);

            // $"
            if (n1 == '"')
            {
                return true;
            }

            // $@"text"
            // @$"text"
            if (n1 == '@' && TextWindow.PeekChar(2) == '"')
            {
                return true;
            }

            if (n1 == '"' &&
                TextWindow.PeekChar(2) == '"' &&
                TextWindow.PeekChar(3) == '"')
            {
                // $""" raw interpolated string
                return true;
            }
        }

        // Verbatim string: @"text"
        return ch == '@' && TextWindow.PeekChar(1) == '"';
    }

    private CodeAnalysisToken ParseCSharpStringOrChar()
    {
        var start = TextWindow.Position;

        if (start < _tokenParserPosition)
        {
            _tokenParser = CSharpSyntaxFactory.CreateTokenParser(TextWindow.Text);
            _tokenParserPosition = 0;
        }

        _tokenParser.SkipForwardTo(start);

        var result = _tokenParser.ParseNextToken();
        var token = result.Token;
        _tokenParserPosition = start + token.FullSpan.Length;

        if(IsCSharpStringOrCharKind(token.Kind()))
        {
            TextWindow.Reset(start + token.FullSpan.Length);
            return token;
        }

        // Shoud make error reporting here

        return default;
    }

    private static bool IsCSharpStringOrCharKind(CSharpSyntaxKind cSharpSyntaxKind)
    {
        return cSharpSyntaxKind == CSharpSyntaxKind.StringLiteralToken ||
               cSharpSyntaxKind == CSharpSyntaxKind.InterpolatedStringTextToken ||
               cSharpSyntaxKind == CSharpSyntaxKind.MultiLineRawStringLiteralToken ||
               cSharpSyntaxKind == CSharpSyntaxKind.InterpolatedMultiLineRawStringStartToken ||
               cSharpSyntaxKind == CSharpSyntaxKind.Utf8MultiLineRawStringLiteralToken ||
               cSharpSyntaxKind == CSharpSyntaxKind.Utf8StringLiteralToken ||
               cSharpSyntaxKind == CSharpSyntaxKind.CharacterLiteralToken;
    }
}
