using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language;
internal sealed class Lexer
{
    /// <summary>
    /// Not readonly. This is a mutable struct that will be modified as we lex tokens.
    /// </summary>
    internal SlidingTextWindow TextWindow;

    internal enum LexerMode
    {
        Default,
        InString,
        InCsharpExpression,
        InCsharpBlock,
        InStartTag,
        InContent,
        InEndTag,
        InAkcss,
        InUtilities,
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

        internal CSharpSyntaxKind CSharpSyntaxKind;
    }

    public Lexer(SourceText sourceText)
    {
        TextWindow = new SlidingTextWindow(sourceText);
    }


}
