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

namespace Akbura.Language;


partial class Lexer
{

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

        _tokenParser.SkipForwardTo(start);

        var result = _tokenParser.ParseNextToken();
        var token = result.Token;

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
