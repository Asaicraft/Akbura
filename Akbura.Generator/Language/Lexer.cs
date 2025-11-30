using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CodeAnalysisSyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;
using Microsoft.CodeAnalysis;

namespace Akbura.Language;

#pragma warning disable RSEXPERIMENTAL003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
internal sealed partial class Lexer
{
    /// <summary>
    /// Not readonly. This is a mutable struct that will be modified as we lex tokens.
    /// </summary>
    internal SlidingTextWindow TextWindow;

    internal enum LexerMode
    {
        TopLevel = 0,

        InInlineExpression = 1 << 0,
        InExpressionUntilSemicolon = 1 << 1,
        InExpressionUntilComma = 1 << 2,
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



    private readonly Microsoft.CodeAnalysis.CSharp.SyntaxTokenParser _tokenParser;

    public Lexer(SourceText sourceText)
    {
        TextWindow = new SlidingTextWindow(sourceText);

        _cache = LexerCache.GetInstance();
        _builder = _cache.StringBuilder;
        _identifierBuffer = _cache.IdentifierBuffer;
        _leadingTriviaCache = _cache.LeadingTriviaCache;
        _trailingTriviaCache = _cache.TrailingTriviaCache;
        _tokenParser = CSharpSyntaxFactory.CreateTokenParser(sourceText);
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
            LexerMode.InExpressionUntilComma => ParseExpressionUntilComma(),
            _ => default,
        };

        return CreateToken(in tokenInfo);
    }

    private TokenInfo ParseInlineExpression()
    {
        return ParseExpressionUntil(')');
    }

    private TokenInfo ParseExpressionUntilSemicolon()
    {
        return ParseExpressionUntil(';');
    }

    private TokenInfo ParseExpressionUntilComma()
    {
        return ParseExpressionUntil(',');
    }

    private TokenInfo ParseExpressionUntil(char terminator)
    {
        static void IncreaseDepth(ref int depth, char character, StringBuilder stringBuilder, in SlidingTextWindow TextWindow)
        {
            depth++;
            stringBuilder.Append(character);
            TextWindow.NextChar();
        }

        static bool DecreaseDepth(ref int depth, char character, StringBuilder stringBuilder, in SlidingTextWindow TextWindow)
        {
            depth--;
            stringBuilder.Append(character);
            TextWindow.NextChar();

            if (depth < 0)
            {
                depth = 0;
                return true;
            }

            return false;
        }

        // We do NOT intern. Raw expression text goes directly into Roslyn C# parser.
        var tokenInfo = new TokenInfo
        {
            Kind = SyntaxKind.CSharpRawToken,
            ContextualKind = SyntaxKind.CSharpRawToken
        };

        // We assume the token that starts the expression (e.g. '=' or '(' or ',') has already been consumed.
        var expressionOffset = TextWindow.Position;

        _builder.Clear();

        var paren = 0;   // (...)
        var brace = 0;   // {...}
        var bracket = 0; // [...]

        while (true)
        {
            var character = TextWindow.PeekChar();

            if (character == SlidingTextWindow.InvalidCharacter)
            {
                // Unterminated expression, return what we have.
                break;
            }

            // strings/chars via Roslyn
            if (ScanCSharpStringOrChar())
            {
                var token = ParseCSharpStringOrChar();

                if (token.RawKind != 0)
                {
                    _builder.Append(token.Text);
                    continue;
                }

                break;
            }

            // structure tracking
            if (character == '(')
            {
                IncreaseDepth(ref paren, character, _builder, in TextWindow);
                continue;
            }

            if (character == ')')
            {
                if (DecreaseDepth(ref paren, character, _builder, in TextWindow))
                {
                    // Unmatched ')', just break to avoid infinite loop
                    break;
                }

                continue;
            }

            if (character == '{')
            {
                IncreaseDepth(ref brace, character, _builder, in TextWindow);
                continue;
            }


            if (character == '}')
            {
                if (DecreaseDepth(ref brace, character, _builder, in TextWindow))
                {
                    // Unmatched '}', just break to avoid infinite loop
                    break;
                }
                continue;
            }

            if (character == '[')
            {
                IncreaseDepth(ref bracket, character, _builder, in TextWindow);
                continue;
            }

            if (character == ']')
            {
                if (DecreaseDepth(ref bracket, character, _builder, in TextWindow))
                {
                    // Unmatched ']', just break to avoid infinite loop
                    break;
                }
                continue;
            }

            // Stop only when fully unnested ---
            if (character == terminator && paren == 0 && brace == 0 && bracket == 0)
            {
                break;
            }

            // normal char 
            _builder.Append(character);
            TextWindow.NextChar();
        }

        var expressionText = _builder.ToString();
        tokenInfo.StringValue = expressionText;

        var parsed = CSharpSyntaxFactory.ParseExpression(
            expressionText,
            expressionOffset,
            options: null,
            consumeFullText: true);

        tokenInfo.CSharpNode = parsed;
        tokenInfo.CSharpSyntaxKind = parsed.Kind();

        return tokenInfo;
    }



    private static GreenSyntaxToken CreateToken(in TokenInfo tokenInfo)
    {
        if (tokenInfo.Kind == SyntaxKind.CSharpRawToken)
        {
            AkburaDebug.AssertNotNull(tokenInfo.CSharpNode);

            return GreenSyntaxToken.CreateCSharpRawToken(tokenInfo.CSharpNode);
        }

        throw new NotImplementedException();
    }
}
#pragma warning restore RSEXPERIMENTAL003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.