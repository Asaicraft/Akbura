using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language;
internal sealed class Lexer
{
    /// <summary>
    /// Not readonly. This is a mutable struct that will be modified as we lex tokens.
    /// </summary>
    internal SlidingTextWindow TextWindow;

    public Lexer(SourceText sourceText)
    {
        TextWindow = new SlidingTextWindow(sourceText);
    }

    internal enum LexerMode
    {
        Default,
        InString,
        InCsharpExpression,
    }

    internal struct TokenInfo
    {
        public readonly LexerMode LexerMode;

        public readonly int Position;

        internal SyntaxKind Kind;
        internal SyntaxKind ContextualKind;
        internal string? Text;
        internal SpecialType ValueKind;
        internal string? StringValue;
        internal char CharValue;
        internal int IntValue;
        internal uint UintValue;
        internal long LongValue;
        internal ulong UlongValue;
        internal float FloatValue;
        internal double DoubleValue;
        internal decimal DecimalValue;
        internal bool IsVerbatim;
    }


}
