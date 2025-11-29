using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CodeAnalysisSyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace Akbura.Language;

internal sealed partial class Lexer
{
    /// <summary>
    /// Not readonly. This is a mutable struct that will be modified as we lex tokens.
    /// </summary>
    internal SlidingTextWindow TextWindow;

    internal enum LexerMode
    {
        TopLevel = 0,

        InInlineExpression         = 1 << 0,
        InExpressionUntilSemicolon = 1 << 1,
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
        internal CodeAnalysisSyntaxNode? CSharpNode;
    }

    private LexerMode _mode;
    private readonly StringBuilder _builder;
    private char[] _identifierBuffer;
    private int _identifierLength;
    private readonly LexerCache _cache;
    private int _badTokenCount; // cumulative count of bad tokens produced

    private SyntaxListBuilder _leadingTriviaCache;
    private SyntaxListBuilder _trailingTriviaCache;
    private SyntaxListBuilder? _directiveTriviaCache;

    public Lexer(SourceText sourceText)
    {
        TextWindow = new SlidingTextWindow(sourceText);

        _cache = LexerCache.GetInstance();
        _builder = _cache.StringBuilder;
        _identifierBuffer = _cache.IdentifierBuffer;
        _leadingTriviaCache = _cache.LeadingTriviaCache;
        _trailingTriviaCache = _cache.TrailingTriviaCache;
    }

    public GreenSyntaxToken Lex(ref LexerMode mode)
    {
        var result = Lex(mode);
        mode = _mode;
        return result;
    }

#if DEBUG
    internal static int TokensLexed;
#endif

    public GreenSyntaxToken Lex(LexerMode mode)
    {
#if DEBUG
        TokensLexed++;
#endif
        _mode = mode;
        
        var tokenInfo = mode switch
        {
            LexerMode.InInlineExpression => ParseInlineExpression(),
            LexerMode.InExpressionUntilSemicolon => ParseExpressionUntilSemicolon(),
            _ => default,
        };

        return CreateToken(in tokenInfo);
    }

    private TokenInfo ParseInlineExpression()
    {
        // Do not intern strings here.
        // Inline expression text is passed directly to the C# parser, which handles
        // its own internal caching and deduplication. Interleaving Akbura-level
        // string interning with Roslyn's intern tables is unnecessary and may
        // reduce performance by increasing string churn.

        var tokenInfo = new TokenInfo
        {
            Kind = SyntaxKind.CSharpRawToken,
            ContextualKind = SyntaxKind.CSharpRawToken
        };

        // We assume the opening '{' has already been consumed.
        var expressionOffset = TextWindow.Position;

        _builder.Clear();

        var depth = 0;

        while (true)
        {
            var character = TextWindow.PeekChar();

            if (character == SlidingTextWindow.InvalidCharacter)
            {
                // Unterminated inline expression, return what we have.
                break;
            }

            if (character == '{')
            {
                depth++;
                _builder.Append(character);
                TextWindow.NextChar();
                continue;
            }

            if (character == '}')
            {
                if (depth == 0)
                {
                    // Do not consume the final '}', let the caller handle it.
                    break;
                }

                depth--;
                _builder.Append(character);
                TextWindow.NextChar();
                continue;
            }

            _builder.Append(character);
            TextWindow.NextChar();
        }

        var expressionText = _builder.ToString();
        tokenInfo.StringValue = expressionText;

        var expression = CSharpSyntaxFactory.ParseExpression(
            expressionText,
            expressionOffset,
            options: null,
            consumeFullText: true);

        tokenInfo.CSharpNode = expression;
        tokenInfo.CSharpSyntaxKind = expression.Kind();

        return tokenInfo;
    }

    private TokenInfo ParseExpressionUntilSemicolon()
    {
        // We do NOT intern. Raw expression text goes directly into Roslyn C# parser.
        var tokenInfo = new TokenInfo
        {
            Kind = SyntaxKind.CSharpRawToken,
            ContextualKind = SyntaxKind.CSharpRawToken
        };

        // We assume the '=' token has already been consumed.
        var expressionOffset = TextWindow.Position;

        _builder.Clear();

        var parenDepth = 0;   // (...)
        var braceDepth = 0;   // {...}
        var bracketDepth = 0; // [...]
        var inString = false;
        var stringQuote = '\0';

        while (true)
        {
            var character = TextWindow.PeekChar();

            if (character == SlidingTextWindow.InvalidCharacter)
            {
                // Unterminated inline expression, return what we have.
                break; 
            }

            // -------------------------------------------------------
            // Handle entering/exiting string literals
            // -------------------------------------------------------
            if (inString)
            {
                _builder.Append(character);
                TextWindow.NextChar();

                if (character == stringQuote)
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"' || character == '\'')
            {
                inString = true;
                stringQuote = character;
                _builder.Append(character);
                TextWindow.NextChar();
                continue;
            }

            // -------------------------------------------------------
            // Track nesting
            // -------------------------------------------------------
            if (character == '(') { parenDepth++; _builder.Append(character); TextWindow.NextChar(); continue; }
            if (character == ')') { parenDepth--; _builder.Append(character); TextWindow.NextChar(); continue; }

            if (character == '{') { braceDepth++; _builder.Append(character); TextWindow.NextChar(); continue; }
            if (character == '}') { braceDepth--; _builder.Append(character); TextWindow.NextChar(); continue; }

            if (character == '[') { bracketDepth++; _builder.Append(character); TextWindow.NextChar(); continue; }
            if (character == ']') { bracketDepth--; _builder.Append(character); TextWindow.NextChar(); continue; }

            // -------------------------------------------------------
            // Stop at semicolon ONLY if not nested
            // -------------------------------------------------------
            if (character == ';' && parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
            {
                // DO NOT consume the semicolon
                break;
            }

            // default: append normal char
            _builder.Append(character);
            TextWindow.NextChar();
        }

        var exprText = _builder.ToString();
        tokenInfo.StringValue = exprText;

        var parsed = CSharpSyntaxFactory.ParseExpression(
            exprText,
            expressionOffset,
            options: null,
            consumeFullText: true);

        tokenInfo.CSharpNode = parsed;
        tokenInfo.CSharpSyntaxKind = parsed.Kind();

        return tokenInfo;
    }

    private static GreenSyntaxToken CreateToken(in TokenInfo tokenInfo)
    {
        if(tokenInfo.Kind == SyntaxKind.CSharpRawToken)
        {
            AkburaDebug.AssertNotNull(tokenInfo.CSharpNode);

            return GreenSyntaxToken.CreateCSharpRawToken(tokenInfo.CSharpNode);
        }

        throw new NotImplementedException();
    }
}
