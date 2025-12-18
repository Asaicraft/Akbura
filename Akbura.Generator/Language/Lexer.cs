// THis file is ported and adopted from roslyn

using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CodeAnalysisSyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Globalization;
using CodeAnalysis = Microsoft.CodeAnalysis;

namespace Akbura.Language;

#pragma warning disable RSEXPERIMENTAL003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
internal sealed partial class Lexer : IDisposable
{
    // Akbura currently lacks real-world profiling data for token sizes,
    // so we adopt Roslyn’s empirical limit: tokens over ~40 chars are rare.
    // 42 serves as a practical upper bound for our cache.
    // https://github.com/dotnet/roslyn/blob/c14edd18895fe53efd010d4517da332f30784df6/src/Compilers/CSharp/Portable/Parser/QuickScanner.cs#L17
    internal const int MaxCachedTokenSize = 42;


    private const int TriviaListInitialCapacity = 8;

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
        InArgumentExpression = 1 << 3,
        InMarkup = 1 << 4,
        InTypeName = 1 << 5,
        InAkcss = 1 << 6,
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
        internal bool HasIdentifierEscapeSequence;

        internal CSharpSyntaxKind CSharpSyntaxKind;
        internal CodeAnalysisSyntaxNode? CSharpNode;
    }

    private LexerMode _mode;
    private readonly StringBuilder _builder;
    private char[] _identifierBuffer;
    private int _identifierLength;
    private readonly LexerCache _cache;
    private int _badTokenCount; // cumulative count of bad tokens produced

    private GreenSyntaxListBuilder _leadingTriviaCache;
    private GreenSyntaxListBuilder _trailingTriviaCache;

    private List<AkburaDiagnostic>? _errors;

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

    public void Dispose()
    {
        _cache.Free();
        this.TextWindow.Free();
    }

    public GreenSyntaxToken Lex(LexerMode mode)
    {
#if DEBUG
        TokensLexed++;
#endif
        _mode = mode;

        if (mode != LexerMode.TopLevel)
        {
            var tokenInfo = mode switch
            {
                LexerMode.InInlineExpression => ParseInlineExpression(),
                LexerMode.InExpressionUntilSemicolon => ParseExpressionUntilSemicolon(),
                LexerMode.InExpressionUntilComma => ParseExpressionUntilComma(),
                LexerMode.InArgumentExpression => ParseArgumentExpression(),
                LexerMode.InTypeName => ParseTypeName(),
                _ => default
            };

            // In expression modes, we do not care about trivia or errors.
            return CreateToken(in tokenInfo, null, null, null);
        }

        return ParseNextToken();
    }

    private GreenSyntaxToken ParseNextToken()
    {
        var tokenInfo = ParseNextTokenInfo(out var leading, out var trailing, out var errors);

        return CreateToken(in tokenInfo, leading, trailing, errors);
    }

    private TokenInfo ParseNextTokenInfo(out GreenSyntaxListBuilder leading, out GreenSyntaxListBuilder trailing, out ImmutableArray<AkburaDiagnostic> errors)
    {
        _leadingTriviaCache.Clear();
        LexSyntaxTrivia(isTrailing: false, triviaList: ref _leadingTriviaCache);
        leading = _leadingTriviaCache;

        TokenInfo tokenInfo = default;

        Start();
        ParseSyntaxToken(ref tokenInfo);
        errors = GetErrors();

        _trailingTriviaCache.Clear();
        LexSyntaxTrivia(isTrailing: true, triviaList: ref _trailingTriviaCache);
        trailing = _trailingTriviaCache;

        return tokenInfo;
    }

    private void ParseSyntaxToken(ref TokenInfo info)
    {
        // Reset token info for a new scan.
        info.Kind = SyntaxKind.None;
        info.ContextualKind = SyntaxKind.None;
        info.Text = null;
        info.StringValue = null;
        info.ValueKind = SpecialType.None;
        info.IsVerbatim = false;
        info.HasIdentifierEscapeSequence = false;

        var isEscaped = false;
        var startingPosition = TextWindow.Position;

        var character = TextWindow.PeekChar();

        // End-of-file handling
        if (character == SlidingTextWindow.InvalidCharacter)
        {
            if (!TextWindow.IsReallyAtEnd())
            {
                ConsumeUnexpected(ref info, startingPosition, isEscaped);
                return;
            }

            info.Kind = SyntaxKind.EndOfFileToken;
            return;
        }

        switch (character)
        {
            // Quote tokens for markup / AKCSS
            case '\'':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.SingleQuoteToken;
                return;

            case '"':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.DoubleQuoteToken;
                return;

            // Slash: "/", "/>"
            case '/':
            {
                TextWindow.AdvanceChar();

                if (TextWindow.TryAdvance('>'))
                {
                    info.Kind = SyntaxKind.SlashGreaterToken; // "/>"
                    return;
                }

                info.Kind = SyntaxKind.SlashToken; // "/"
                return;
            }

            // Dot / DoubleDot: "." / ".."
            case '.':
            {
                if (TextWindow.PeekChar(1) == '.')
                {
                    TextWindow.AdvanceChar(2);
                    info.Kind = SyntaxKind.DoubleDotToken; // ".."
                    return;
                }

                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.DotToken; // "."
                return;
            }

            // Punctuation: comma / colon / semicolon / question
            case ',':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.CommaToken;
                return;

            case ':':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.ColonToken;
                return;

            case ';':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.SemicolonToken;
                return;

            case '?':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.QuestionToken;
                return;

            // Arithmetic / bitwise operators
            case '+':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.PlusToken;
                return;

            case '-':
            {
                TextWindow.AdvanceChar();

                info.Kind = SyntaxKind.MinusToken;
                return;
            }

            case '*':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.AsteriskToken;
                return;

            case '%':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.PercentToken;
                return;

            case '^':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.CaretToken;
                return;

            case '&':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.AmpersandToken;
                return;

            case '|':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.BarToken;
                return;

            // Equals / "==" / "=>"
            case '=':
            {
                TextWindow.AdvanceChar();

                if (TextWindow.TryAdvance('>'))
                {
                    info.Kind = SyntaxKind.ArrowToken; // "=>"
                    return;
                }

                if (TextWindow.TryAdvance('='))
                {
                    info.Kind = SyntaxKind.EqualsEqualsToken; // "=="
                    return;
                }

                info.Kind = SyntaxKind.EqualsToken; // "="
                return;
            }

            // Bang / "!="
            case '!':
            {
                TextWindow.AdvanceChar();

                if (TextWindow.TryAdvance('='))
                {
                    info.Kind = SyntaxKind.BangEqualsToken; // "!="
                    return;
                }

                info.Kind = SyntaxKind.BangToken; // "!"
                return;
            }

            // Comparison / markup open: "<", "<=", "</"
            case '<':
            {
                TextWindow.AdvanceChar();

                if (TextWindow.TryAdvance('/'))
                {
                    info.Kind = SyntaxKind.LessSlashToken; // "</"
                    return;
                }

                if (TextWindow.TryAdvance('='))
                {
                    info.Kind = SyntaxKind.LessEqualsToken; // "<="
                    return;
                }

                info.Kind = SyntaxKind.LessThanToken; // "<"
                return;
            }

            case '>':
            {
                TextWindow.AdvanceChar();

                if (TextWindow.TryAdvance('='))
                {
                    info.Kind = SyntaxKind.GreaterEqualsToken; // ">="
                    return;
                }

                info.Kind = SyntaxKind.GreaterThanToken; // ">"
                return;
            }

            // Braces / brackets / parens
            case '{':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.OpenBraceToken;
                return;

            case '}':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.CloseBraceToken;
                return;

            case '[':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.OpenBracketToken;
                return;

            case ']':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.CloseBracketToken;
                return;

            case '(':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.OpenParenToken;
                return;

            case ')':
                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.CloseParenToken;
                return;

            // Akcss / utilities 
            case '@':

                if ((_mode & LexerMode.InAkcss) == 0)
                {
                    if (this.ScanIdentifierOrKeyword(ref info))
                    {
                        return;
                    }
                }

                TextWindow.AdvanceChar();
                info.Kind = SyntaxKind.AtToken;
                return;


            // Numeric literal: used in Tailwind segments (w-10)
            // and potentially in DSL contexts.
            case >= '0' and <= '9':
                ScanNumericLiteral(ref info);
                return;

            // Identifier / keyword (ASCII fast path)
            case '_':
            case >= 'a' and <= 'z':
            case >= 'A' and <= 'Z':
                ScanIdentifierOrKeyword(ref info);
                return;

            // '\' — possible start of a unicode-escaped identifier
            case '\\':
            {
                isEscaped = true;

                var peeked = PeekCharOrUnicodeEscape(out _);
                if (SyntaxFacts.IsIdentifierStartCharacter(peeked))
                {
                    if (ScanIdentifierOrKeyword(ref info))
                    {
                        return;
                    }
                }

                goto default;
            }


            default:
                // Non-ASCII identifier start
                if (SyntaxFacts.IsIdentifierStartCharacter(character))
                {
                    goto case '_';
                }

                // Unknown / invalid character => error + recovery
                ConsumeUnexpected(ref info, startingPosition, isEscaped);
                return;
        }
    }

    private bool ScanIdentifierOrKeyword(ref TokenInfo info)
    {
        // Reset contextual kind for each identifier
        info.ContextualKind = SyntaxKind.None;

        if (!TryParseIdentifier(ref info))
        {
            info.Kind = SyntaxKind.None;
            return false;
        }

        // At this point TryParseIdentifier has filled info.Text / StringValue etc.
        AkburaDebug.Assert(info.Text is not null);

        // If the identifier is escaped or verbatim, it can never be a keyword.
        if (info.IsVerbatim || info.HasIdentifierEscapeSequence)
        {
            info.Kind = SyntaxKind.IdentifierToken;
            info.ContextualKind = SyntaxKind.None;
            return true;
        }

        // Akbura does not have directive mode. We always use the regular keyword table.
        if (_cache.TryGetKeywordKind(info.Text, out var keywordKind))
        {
            if (SyntaxFacts.IsContextualKeyword(keywordKind))
            {
                // Let the parser decide based on context:
                // lexically this is still an identifier, but we carry the contextual kind.
                info.Kind = SyntaxKind.IdentifierToken;
                info.ContextualKind = keywordKind;
            }
            else
            {
                // Hard keyword at the token level.
                info.Kind = keywordKind;
                info.ContextualKind = keywordKind;
            }
        }
        else
        {
            info.Kind = SyntaxKind.IdentifierToken;
            info.ContextualKind = SyntaxKind.None;
        }

        if (info.Kind == SyntaxKind.None)
        {
            info.Kind = SyntaxKind.IdentifierToken;
        }

        return true;
    }

    private void ConsumeUnexpected(ref TokenInfo info, int startingPosition, bool isEscaped)
    {
        var ch = TextWindow.PeekChar();

        if (ch != SlidingTextWindow.InvalidCharacter)
        {
            TextWindow.AdvanceChar();

            // If we hit the start of a surrogate pair, consume both chars
            if (char.IsHighSurrogate(ch) && char.IsLowSurrogate(TextWindow.PeekChar()))
            {
                TextWindow.AdvanceChar();
            }
        }

        if (_badTokenCount++ <= 200)
        {
            info.Text = GetInternedLexemeText();
        }
        else
        {
            var end = TextWindow.Text.Length;
            info.Text = TextWindow.Text.ToString(TextSpan.FromBounds(startingPosition, end));
            TextWindow.Reset(end);
        }

        // For now we classify it as an identifier-like bad token so that CreateToken can handle it.
        info.Kind = SyntaxKind.IdentifierToken;

        // If the original text wasn't already escaped, then escape non-printable chars in the message.
        var messageText = info.Text;
        if (!isEscaped && messageText is not null)
        {
            messageText = ObjectDisplay.FormatLiteral(
                messageText,
                ObjectDisplayOptions.EscapeNonPrintableCharacters
            );
        }

        // For now we ignore messageText in the diagnostic payload.
        // It can be wired as a diagnostic parameter later if needed.
        AddError(ErrorCodes.ERR_UnexpectedCharacter, [messageText]);
    }

    private void Start()
    {
        LexemeStartPosition = this.TextWindow.Position;
        _errors = null;
    }

    #region Lexeme Utilities

    internal int LexemeStartPosition;

    internal int CurrentLexemeWidth => this.TextWindow.Position - LexemeStartPosition;

    internal string GetInternedLexemeText()
            => TextWindow.GetText(LexemeStartPosition, intern: true);

    internal string GetNonInternedLexemeText()
            => TextWindow.GetText(LexemeStartPosition, intern: false);

    #endregion

    #region Numeric

    private bool ScanNumericLiteral(ref TokenInfo info)
    {
        var start = TextWindow.Position;
        char character;
        var isHex = false;
        var isBinary = false;
        var hasDecimal = false;
        var hasExponent = false;
        info.Text = null;
        info.ValueKind = SpecialType.None;
        _builder.Clear();
        var hasUSuffix = false;
        var hasLSuffix = false;
        var underscoreInWrongPlace = false;
        var usedUnderscore = false;
        var firstCharWasUnderscore = false;

        character = TextWindow.PeekChar();
        if (character == '0')
        {
            character = TextWindow.PeekChar(1);
            if (character == 'x' || character == 'X')
            {
                TextWindow.AdvanceChar(2);
                isHex = true;
            }
            else if (character == 'b' || character == 'B')
            {
                TextWindow.AdvanceChar(2);
                isBinary = true;
            }
        }

        if (isHex || isBinary)
        {
            // It's OK if it has no digits after the '0x' -- we'll catch it in ScanNumericLiteral
            // and give a proper error then.
            ScanNumericLiteralSingleInteger(ref underscoreInWrongPlace, ref usedUnderscore, ref firstCharWasUnderscore, isHex, isBinary);

            if (TextWindow.PeekChar() is 'L' or 'l')
            {
                TextWindow.AdvanceChar();
                hasLSuffix = true;
                if (TextWindow.PeekChar() is 'u' or 'U')
                {
                    TextWindow.AdvanceChar();
                    hasUSuffix = true;
                }
            }
            else if (TextWindow.PeekChar() is 'u' or 'U')
            {
                TextWindow.AdvanceChar();
                hasUSuffix = true;
                if (TextWindow.PeekChar() is 'L' or 'l')
                {
                    TextWindow.AdvanceChar();
                    hasLSuffix = true;
                }
            }
        }
        else
        {
            ScanNumericLiteralSingleInteger(ref underscoreInWrongPlace, ref usedUnderscore, ref firstCharWasUnderscore, isHex: false, isBinary: false);

            if ((character = TextWindow.PeekChar()) == '.')
            {
                var ch2 = TextWindow.PeekChar(1);
                if (ch2 >= '0' && ch2 <= '9')
                {
                    hasDecimal = true;
                    _builder.Append(character);
                    TextWindow.AdvanceChar();

                    ScanNumericLiteralSingleInteger(ref underscoreInWrongPlace, ref usedUnderscore, ref firstCharWasUnderscore, isHex: false, isBinary: false);
                }
                else if (_builder.Length == 0)
                {
                    // we only have the dot so far.. (no preceding number or following number)
                    TextWindow.Reset(start);
                    return false;
                }
            }

            if ((character = TextWindow.PeekChar()) is 'E' or 'e')
            {
                _builder.Append(character);
                TextWindow.AdvanceChar();
                hasExponent = true;
                if ((character = TextWindow.PeekChar()) is '-' or '+')
                {
                    _builder.Append(character);
                    TextWindow.AdvanceChar();
                }

                if (!(((character = TextWindow.PeekChar()) >= '0' && character <= '9') || character == '_'))
                {
                    // use this for now (CS0595), cant use CS0594 as we dont know 'type'
                    this.AddError(ErrorCodes.ERR_InvalidReal);
                    // add dummy exponent, so parser does not blow up
                    _builder.Append('0');
                }
                else
                {
                    ScanNumericLiteralSingleInteger(ref underscoreInWrongPlace, ref usedUnderscore, ref firstCharWasUnderscore, isHex: false, isBinary: false);
                }
            }

            character = TextWindow.PeekChar();
            if (hasExponent || hasDecimal)
            {
                if (character is 'f' or 'F')
                {
                    TextWindow.AdvanceChar();
                    info.ValueKind = SpecialType.System_Single;
                }
                else if (character is 'D' or 'd')
                {
                    TextWindow.AdvanceChar();
                    info.ValueKind = SpecialType.System_Double;
                }
                else if (character is 'm' or 'M')
                {
                    TextWindow.AdvanceChar();
                    info.ValueKind = SpecialType.System_Decimal;
                }
                else
                {
                    info.ValueKind = SpecialType.System_Double;
                }
            }
            else if (character is 'f' or 'F')
            {
                TextWindow.AdvanceChar();
                info.ValueKind = SpecialType.System_Single;
            }
            else if (character is 'D' or 'd')
            {
                TextWindow.AdvanceChar();
                info.ValueKind = SpecialType.System_Double;
            }
            else if (character is 'm' or 'M')
            {
                TextWindow.AdvanceChar();
                info.ValueKind = SpecialType.System_Decimal;
            }
            else if (character is 'L' or 'l')
            {
                TextWindow.AdvanceChar();
                hasLSuffix = true;
                if (TextWindow.PeekChar() is 'u' or 'U')
                {
                    TextWindow.AdvanceChar();
                    hasUSuffix = true;
                }
            }
            else if (character == 'u' || character == 'U')
            {
                hasUSuffix = true;
                TextWindow.AdvanceChar();
                if (TextWindow.PeekChar() is 'L' or 'l')
                {
                    TextWindow.AdvanceChar();
                    hasLSuffix = true;
                }
            }
        }

        if (underscoreInWrongPlace)
        {
            this.AddError(ErrorCodes.ERR_InvalidNumber, [start, TextWindow.Position - start]);
        }

        info.Kind = SyntaxKind.NumericLiteralToken;
        info.Text = this.GetInternedLexemeText();
        Debug.Assert(info.Text != null);
        var valueText = TextWindow.Intern(_builder);
        ulong val;
        switch (info.ValueKind)
        {
            case SpecialType.System_Single:
                info.FloatValue = this.GetValueSingle(valueText);
                break;
            case SpecialType.System_Double:
                info.DoubleValue = this.GetValueDouble(valueText);
                break;
            case SpecialType.System_Decimal:
                info.DecimalValue = this.GetValueDecimal(valueText, start, TextWindow.Position);
                break;
            default:
                if (string.IsNullOrEmpty(valueText))
                {
                    if (!underscoreInWrongPlace)
                    {
                        this.AddError(ErrorCodes.ERR_InvalidNumber_WithoutPosition);
                    }
                    val = 0; //safe default
                }
                else
                {
                    val = this.GetValueUInt64(valueText, isHex, isBinary);
                }

                // 2.4.4.2 Integer literals
                // ...
                // The type of an integer literal is determined as follows:

                // * If the literal has no suffix, it has the first of these types in which its value can be represented: int, uint, long, ulong.
                if (!hasUSuffix && !hasLSuffix)
                {
                    if (val <= Int32.MaxValue)
                    {
                        info.ValueKind = SpecialType.System_Int32;
                        info.IntValue = (int)val;
                    }
                    else if (val <= UInt32.MaxValue)
                    {
                        info.ValueKind = SpecialType.System_UInt32;
                        info.UintValue = (uint)val;

                        // TODO: See below, it may be desirable to mark this token
                        // as special for folding if its value is 2147483648.
                    }
                    else if (val <= Int64.MaxValue)
                    {
                        info.ValueKind = SpecialType.System_Int64;
                        info.LongValue = (long)val;
                    }
                    else
                    {
                        info.ValueKind = SpecialType.System_UInt64;
                        info.UlongValue = val;

                        // TODO: See below, it may be desirable to mark this token
                        // as special for folding if its value is 9223372036854775808
                    }
                }
                else if (hasUSuffix && !hasLSuffix)
                {
                    // * If the literal is suffixed by U or u, it has the first of these types in which its value can be represented: uint, ulong.
                    if (val <= UInt32.MaxValue)
                    {
                        info.ValueKind = SpecialType.System_UInt32;
                        info.UintValue = (uint)val;
                    }
                    else
                    {
                        info.ValueKind = SpecialType.System_UInt64;
                        info.UlongValue = val;
                    }
                }

                // * If the literal is suffixed by L or l, it has the first of these types in which its value can be represented: long, ulong.
                else if (!hasUSuffix & hasLSuffix)
                {
                    if (val <= Int64.MaxValue)
                    {
                        info.ValueKind = SpecialType.System_Int64;
                        info.LongValue = (long)val;
                    }
                    else
                    {
                        info.ValueKind = SpecialType.System_UInt64;
                        info.UlongValue = val;

                        // TODO: See below, it may be desirable to mark this token
                        // as special for folding if its value is 9223372036854775808
                    }
                }

                // * If the literal is suffixed by UL, Ul, uL, ul, LU, Lu, lU, or lu, it is of type ulong.
                else
                {
                    Debug.Assert(hasUSuffix && hasLSuffix);
                    info.ValueKind = SpecialType.System_UInt64;
                    info.UlongValue = val;
                }

                break;

                // Note, the following portion of the spec is not implemented here. It is implemented
                // in the unary minus analysis.

                // * When a decimal-integer-literal with the value 2147483648 (231) and no integer-type-suffix appears
                //   as the token immediately following a unary minus operator token (§7.7.2), the result is a constant
                //   of type int with the value −2147483648 (−231). In all other situations, such a decimal-integer-
                //   literal is of type uint.
                // * When a decimal-integer-literal with the value 9223372036854775808 (263) and no integer-type-suffix
                //   or the integer-type-suffix L or l appears as the token immediately following a unary minus operator
                //   token (§7.7.2), the result is a constant of type long with the value −9223372036854775808 (−263).
                //   In all other situations, such a decimal-integer-literal is of type ulong.
        }

        return true;
    }

    // Allows underscores in integers, except at beginning for decimal and end
    private void ScanNumericLiteralSingleInteger(ref bool underscoreInWrongPlace, ref bool usedUnderscore, ref bool firstCharWasUnderscore, bool isHex, bool isBinary)
    {
        if (TextWindow.PeekChar() == '_')
        {
            if (isHex || isBinary)
            {
                firstCharWasUnderscore = true;
            }
            else
            {
                underscoreInWrongPlace = true;
            }
        }

        var lastCharWasUnderscore = false;
        while (true)
        {
            var character = TextWindow.PeekChar();
            if (character == '_')
            {
                usedUnderscore = true;
                lastCharWasUnderscore = true;
            }
            else if (!(isHex ? SyntaxFacts.IsHexDigit(character) :
                       isBinary ? SyntaxFacts.IsBinaryDigit(character) :
                       SyntaxFacts.IsDecDigit(character)))
            {
                break;
            }
            else
            {
                _builder.Append(character);
                lastCharWasUnderscore = false;
            }
            TextWindow.AdvanceChar();
        }

        if (lastCharWasUnderscore)
        {
            underscoreInWrongPlace = true;
        }
    }

    private float GetValueSingle(string text)
    {
        if (!RealParser.TryParseFloat(text, out var result))
        {
            //we've already lexed the literal, so the error must be from overflow
            this.AddError(ErrorCodes.ERR_FloatOverflow, ["float"]);
        }

        return result;
    }

    private double GetValueDouble(string text)
    {
        if (!RealParser.TryParseDouble(text, out var result))
        {
            //we've already lexed the literal, so the error must be from overflow
            this.AddError(ErrorCodes.ERR_FloatOverflow, ["double"]);
        }

        return result;
    }

    private ulong GetValueUInt64(string text, bool isHex, bool isBinary)
    {
        ulong result;
        if (isBinary)
        {
            if (!TryParseBinaryUInt64(text, out result))
            {
                this.AddError(ErrorCodes.ERR_IntOverflow);
            }
        }
        else if (!UInt64.TryParse(text, isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out result))
        {
            //we've already lexed the literal, so the error must be from overflow
            this.AddError(ErrorCodes.ERR_IntOverflow);
        }

        return result;
    }

    private decimal GetValueDecimal(string text, int start, int end)
    {
        // Use decimal.TryParse to parse value. Note: the behavior of
        // decimal.TryParse differs from Dev11 in several cases:
        //
        // 1. [-]0eNm where N > 0
        //     The native compiler ignores sign and scale and treats such cases
        //     as 0e0m. decimal.TryParse fails so these cases are compile errors.
        //     [Bug #568475]
        // 2. 1e-Nm where N >= 1000
        //     The native compiler reports CS0594 "Floating-point constant is
        //     outside the range of type 'decimal'". decimal.TryParse allows
        //     N >> 1000 but treats decimals with very small exponents as 0.
        //     [No bug.]
        // 3. Decimals with significant digits below 1e-49
        //     The native compiler considers digits below 1e-49 when rounding.
        //     decimal.TryParse ignores digits below 1e-49 when rounding. This
        //     last difference is perhaps the most significant since existing code
        //     will continue to compile but constant values may be rounded differently.
        //     (Note that the native compiler does not round in all cases either since
        //     the native compiler chops the string at 50 significant digits. For example
        //     ".100000000000000000000000000050000000000000000000001m" is not
        //     rounded up to 0.1000000000000000000000000001.)
        //     [Bug #568494]

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var result))
        {
            //we've already lexed the literal, so the error must be from overflow
            this.AddError(start, end - start, ErrorCodes.ERR_FloatOverflow, ["decimal"]);
        }

        return result;
    }

    // TODO: Change to Int64.TryParse when it supports NumberStyles.AllowBinarySpecifier (inline this method into GetValueUInt32/64)
    private static bool TryParseBinaryUInt64(string text, out ulong value)
    {
        value = 0;
        foreach (var character in text)
        {
            // if uppermost bit is set, then the next bitshift will overflow
            if ((value & 0x8000000000000000) != 0)
            {
                return false;
            }
            // We shouldn't ever get a string that's nonbinary (see ScanNumericLiteral),
            // so don't explicitly check for it (there's a debug assert in SyntaxFacts)
            var bit = (ulong)SyntaxFacts.BinaryValue(character);
            value = (value << 1) | bit;
        }
        return true;
    }

    #endregion

    #region Trivia

    internal SyntaxTriviaList LexSyntaxLeadingTrivia()
    {
        _leadingTriviaCache.Clear();
        LexSyntaxTrivia(isTrailing: false, triviaList: ref _leadingTriviaCache);
        return new SyntaxTriviaList(
            token: default,
            node: _leadingTriviaCache.ToListNode(),
            position: 0,
            index: 0
        );
    }

    internal SyntaxTriviaList LexSyntaxTrailingTrivia()
    {
        _trailingTriviaCache.Clear();
        LexSyntaxTrivia(isTrailing: true, triviaList: ref _trailingTriviaCache);
        return new SyntaxTriviaList(
            token: default,
            node: _trailingTriviaCache.ToListNode(),
            position: 0,
            index: 0
        );
    }

    /// <summary>
    /// Lexes trivia: whitespace, newline, // comments, /* comments */.
    /// No structural trivia: no XML doc, no directives.
    /// </summary>
    private void LexSyntaxTrivia(bool isTrailing, ref GreenSyntaxListBuilder triviaList)
    {
        while (true)
        {
            Start();

            var character = TextWindow.PeekChar();
            if (character == SlidingTextWindow.InvalidCharacter)
            {
                return;
            }

            // Normalize unicode whitespace
            if (character > 127)
            {
                if (SyntaxFacts.IsWhitespace(character))
                {
                    character = ' ';
                }
                else if (SyntaxFacts.IsNewLine(character))
                {
                    character = '\n';
                }
            }

            switch (character)
            {
                case ' ':
                case '\t':
                case '\v':
                case '\f':
                case '\u001A':
                    AddTrivia(ScanWhitespace(), ref triviaList);
                    break;

                case '\r':
                case '\n':
                    var eol = ScanEndOfLine();
                    AddTrivia(eol, ref triviaList);

                    // Trailing trivia stops on newline
                    if (isTrailing)
                    {
                        return;
                    }

                    break;

                case '/':
                {
                    var next = TextWindow.PeekChar(1);

                    // Single line comment //
                    if (next == '/')
                    {
                        LexSingleLineComment(ref triviaList);
                        break;
                    }

                    // Multi-line comment /* ... */
                    if (next == '*')
                    {
                        LexMultiLineComment(ref triviaList);
                        break;
                    }

                    // Not a trivia
                    return;
                }

                default:
                    // Stop lexing trivia
                    return;
            }
        }

        void LexSingleLineComment(ref GreenSyntaxListBuilder list)
        {
            // Consume "//"
            TextWindow.AdvanceChar(2);

            // Read until newline
            while (true)
            {
                var character = TextWindow.PeekChar();
                if (character == '\r' || character == '\n' || character == SlidingTextWindow.InvalidCharacter)
                {
                    break;
                }

                TextWindow.AdvanceChar();
            }

            var text = GetNonInternedLexemeText();
            AddTrivia(GreenSyntaxFactory.Comment(text), ref list);
        }

        void LexMultiLineComment(ref GreenSyntaxListBuilder list)
        {
            // Consume "/*"
            TextWindow.AdvanceChar(2);
            var terminated = false;

            while (true)
            {
                var character = TextWindow.PeekChar();
                if (character == SlidingTextWindow.InvalidCharacter)
                {
                    break;
                }

                if (character == '*' && TextWindow.PeekChar(1) == '/')
                {
                    TextWindow.AdvanceChar(2);
                    terminated = true;
                    break;
                }

                TextWindow.AdvanceChar();
            }

            if (!terminated)
            {
                AddError(ErrorCodes.ERR_OpenEndedComment);
            }

            var text = GetNonInternedLexemeText();
            AddTrivia(GreenSyntaxFactory.MultiLineComment(text), ref list);
        }
    }

    private void AddTrivia(GreenNode? trivia, [NotNull] ref GreenSyntaxListBuilder? list)
    {
        if (this.HasErrors)
        {
            trivia = trivia?.WithDiagnostics(this.GetErrors());
        }

        list ??= new GreenSyntaxListBuilder(TriviaListInitialCapacity);

        list.Add(trivia);
    }

    /// <summary>
    /// Scans all of the whitespace (not new-lines) into a trivia node until it runs out.
    /// </summary>
    /// <returns>A trivia node with the whitespace text</returns>
    private GreenSyntaxTrivia ScanWhitespace()
    {
        Debug.Assert(SyntaxFacts.IsWhitespace(TextWindow.PeekChar()));

        var hashCode = HashCode.FnvOffsetBias;  // FNV base
        var onlySpaces = true;

    top:
        var ch = TextWindow.PeekChar();

        switch (ch)
        {
            case '\t':       // Horizontal tab
            case '\v':       // Vertical Tab
            case '\f':       // Form-feed
            case '\u001A':
                onlySpaces = false;
                goto case ' ';

            case ' ':
                TextWindow.AdvanceChar();
                hashCode = HashCode.CombineFNVHash(hashCode, ch);
                goto top;

            case '\r':      // Carriage Return
            case '\n':      // Line-feed
                break;

            default:
                if (ch > 127 && SyntaxFacts.IsWhitespace(ch))
                {
                    goto case '\t';
                }

                break;
        }

        Debug.Assert(this.CurrentLexemeWidth > 0);

        if (this.CurrentLexemeWidth == 1 && onlySpaces)
        {
            return GreenSyntaxFactory.Space;
        }
        else
        {
            var width = this.CurrentLexemeWidth;

            if (width < MaxCachedTokenSize)
            {
                return _cache.LookupWhitespaceTrivia(
                    TextWindow,
                    this.LexemeStartPosition,
                    hashCode);
            }
            else
            {
                return GreenSyntaxFactory.Whitespace(this.GetInternedLexemeText());
            }
        }
    }

    /// <summary>
    /// Scans a new-line sequence (either a single new-line character or a CR-LF combo).
    /// </summary>
    /// <returns>A trivia node with the new-line text</returns>
    private GreenNode? ScanEndOfLine()
    {
        char character;
        switch (character = TextWindow.PeekChar())
        {
            case '\r':
                TextWindow.AdvanceChar();
                return TextWindow.TryAdvance('\n') ? GreenSyntaxFactory.CarriageReturnLineFeed : GreenSyntaxFactory.CarriageReturn;
            case '\n':
                TextWindow.AdvanceChar();
                return GreenSyntaxFactory.LineFeed;
            default:
                if (SyntaxFacts.IsNewLine(character))
                {
                    TextWindow.AdvanceChar();
                    return GreenSyntaxFactory.EndOfLine(character.ToString());
                }

                return null;
        }
    }

    #endregion

    #region CSharp Expression

    private TokenInfo ParseInlineExpression()
    {
        return ParseExpressionUntil('}');
    }

    private TokenInfo ParseExpressionUntilSemicolon()
    {
        return ParseExpressionUntil(';');
    }

    private TokenInfo ParseExpressionUntilComma()
    {
        return ParseExpressionUntil(',');
    }

    private TokenInfo ParseArgumentExpression()
    {
        return ParseExpressionUntil(',', ')');
    }

    private TokenInfo ParseExpressionUntil(char terminator)
    {
        static void IncreaseDepth(ref int depth, char character, StringBuilder stringBuilder, ref SlidingTextWindow TextWindow)
        {
            depth++;
            stringBuilder.Append(character);
            TextWindow.NextChar();
        }

        static bool DecreaseDepth(ref int depth, char character, StringBuilder stringBuilder, ref SlidingTextWindow TextWindow)
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
                IncreaseDepth(ref paren, character, _builder, ref TextWindow);
                continue;
            }

            if (character == ')')
            {
                if (DecreaseDepth(ref paren, character, _builder, ref TextWindow))
                {
                    // Unmatched ')', just break to avoid infinite loop
                    break;
                }

                continue;
            }

            if (character == '{')
            {
                IncreaseDepth(ref brace, character, _builder, ref TextWindow);
                continue;
            }


            if (character == '}')
            {
                if (DecreaseDepth(ref brace, character, _builder, ref TextWindow))
                {
                    // Unmatched '}', just break to avoid infinite loop
                    break;
                }
                continue;
            }

            if (character == '[')
            {
                IncreaseDepth(ref bracket, character, _builder, ref TextWindow);
                continue;
            }

            if (character == ']')
            {
                if (DecreaseDepth(ref bracket, character, _builder, ref TextWindow))
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
        tokenInfo.Text = expressionText;

        var parsed = CSharpSyntaxFactory.ParseExpression(
            expressionText,
            0,
            options: null,
            consumeFullText: true);

        tokenInfo.CSharpNode = parsed;
        tokenInfo.CSharpSyntaxKind = parsed.Kind();

        return tokenInfo;
    }

    private TokenInfo ParseExpressionUntil(char firstTerminator, char secondTerminator)
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

        var tokenInfo = new TokenInfo
        {
            Kind = SyntaxKind.CSharpRawToken,
            ContextualKind = SyntaxKind.CSharpRawToken
        };

        var expressionOffset = TextWindow.Position;
        _builder.Clear();

        var paren = 0;
        var brace = 0;
        var bracket = 0;

        while (true)
        {
            var character = TextWindow.PeekChar();

            if (character == SlidingTextWindow.InvalidCharacter)
                break;

            // handle strings/chars
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

            // structure: (
            if (character == '(')
            {
                IncreaseDepth(ref paren, character, _builder, in TextWindow);
                continue;
            }

            if (character == ')')
            {
                if (DecreaseDepth(ref paren, character, _builder, in TextWindow))
                    break;
                continue;
            }

            // structure: {
            if (character == '{')
            {
                IncreaseDepth(ref brace, character, _builder, in TextWindow);
                continue;
            }

            if (character == '}')
            {
                if (DecreaseDepth(ref brace, character, _builder, in TextWindow))
                    break;
                continue;
            }

            // structure: [
            if (character == '[')
            {
                IncreaseDepth(ref bracket, character, _builder, in TextWindow);
                continue;
            }

            if (character == ']')
            {
                if (DecreaseDepth(ref bracket, character, _builder, in TextWindow))
                    break;
                continue;
            }

            // NEW: stop if we hit ANY terminator and nesting == 0
            if ((character == firstTerminator || character == secondTerminator) &&
                paren == 0 && brace == 0 && bracket == 0)
            {
                break;
            }

            _builder.Append(character);
            TextWindow.NextChar();
        }

        var expressionText = _builder.ToString();
        tokenInfo.Text = expressionText;

        var parsed = CSharpSyntaxFactory.ParseExpression(
            expressionText,
            expressionOffset,
            options: null,
            consumeFullText: true);

        tokenInfo.CSharpNode = parsed;
        tokenInfo.CSharpSyntaxKind = parsed.Kind();

        return tokenInfo;
    }

    #endregion

    #region CSharp Statements


    #endregion

    #region CSharp TypeSyntax

    private TokenInfo ParseTypeName()
    {
        var parsed = ParseCSharpTypeSlow();

        return new()
        {
            Kind = SyntaxKind.CSharpRawToken,
            CSharpNode = parsed,
            CSharpSyntaxKind = parsed.Kind()
        };
    }

    private CSharp.TypeSyntax ParseCSharpTypeSlow()
    {
        const int OptimisticTypeSyntaxLength = 128;

        var start = TextWindow.Position;

        var optimisticString = TextWindow.GetText(start, OptimisticTypeSyntaxLength, intern: false);

        var parsed = CSharpSyntaxFactory.ParseTypeName(
            optimisticString,
            0,
            options: null,
            consumeFullText: false);

        TextWindow.Reset(start + parsed.FullSpan.Length);

        return parsed;
    }

    #endregion

    #region Identifier

    private bool TryParseIdentifier(ref TokenInfo token)
    {
        if (TryParseIdentifier_Fast(ref token))
        {
            return true;
        }

        return TryParseIdentifier_Slow(ref token);
    }

    /// <summary>
    /// Fast path for simple ASCII identifiers:
    /// <c>[_a-zA-Z][_a-zA-Z0-9]*</c>
    /// Works purely inside <see cref="SlidingTextWindow.CurrentWindowSpan"/>.
    /// Returns <c>false</c> if:
    /// <list type="bullet">
    ///   <item>
    ///     <description>The span is empty</description>
    ///   </item>
    ///   <item>
    ///     <description>The first char is not a valid identifier start</description>
    ///   </item>
    ///   <item>
    ///     <description>The identifier reaches beyond the current window chunk</description>
    ///   </item>
    /// </list>
    /// Will not allocate and will always intern the identifier text.
    /// </summary>
    private bool TryParseIdentifier_Fast(ref TokenInfo token)
    {
        token = default;

        var span = TextWindow.CurrentWindowSpan;
        if (span.IsEmpty)
        {
            return false;
        }

        var currentIndex = 0;
        var character = span[currentIndex];

        // First character must be a letter or underscore.
        if (!(character == '_' ||
              (character >= 'a' && character <= 'z') ||
              (character >= 'A' && character <= 'Z')))
        {
            return false;
        }

        currentIndex++;

        while (true)
        {
            // If we run out of data inside the current window, slow-path must be used.
            if (currentIndex == span.Length)
            {
                return false;
            }

            character = span[currentIndex];

            switch (character)
            {
                // Terminators – identifier ends here.
                case '\0':
                case ' ':
                case '\r':
                case '\n':
                case '\t':
                case '!':
                case '%':
                case '(':
                case ')':
                case '*':
                case '+':
                case ',':
                case '-':
                case '.':
                case '/':
                case ':':
                case ';':
                case '<':
                case '=':
                case '>':
                case '?':
                case '[':
                case ']':
                case '^':
                case '{':
                case '|':
                case '}':
                case '~':
                case '"':
                case '\'':
                case '&':     // Always treated as a terminator in Akbura.
                {
                    var length = currentIndex;

                    TextWindow.AdvanceChar(length);

                    var text = TextWindow.Intern(span[..length]);

                    token.Text = text;
                    token.Kind = SyntaxKind.IdentifierToken;

                    return true;
                }

                // Digits allowed only after the first character.
                case >= '0' and <= '9':
                    if (currentIndex == 0)
                    {
                        return false;
                    }

                    currentIndex++;
                    continue;

                // Valid continuation characters.
                case (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'):
                case '_':
                    currentIndex++;
                    continue;

                // Anything else, including non-ASCII => fallback to slow-path.
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Slow path for identifiers. Handles the following cases:
    /// <list type="bullet">
    ///   <item>
    ///     <description>Unicode letters</description>
    ///   </item>
    ///   <item>
    ///     <description>Unicode escape sequences (<c>\uXXXX</c>, <c>\UXXXXXXXX</c>)</description>
    ///   </item>
    ///   <item>
    ///     <description>Surrogate pairs</description>
    ///   </item>
    ///   <item>
    ///     <description>Formatting characters</description>
    ///   </item>
    ///   <item>
    ///     <description>Escaped identifier sequences</description>
    ///   </item>
    /// </list>
    /// Falls back here whenever FastPath cannot handle the case.
    /// </summary>
    private bool TryParseIdentifier_Slow(ref TokenInfo info)
    {
        var start = TextWindow.Position;
        ResetIdentifierBuffer();

        while (TextWindow.PeekChar() == '@')
        {
            TextWindow.AdvanceChar();
        }

        var atCount = TextWindow.Position - start;
        info.IsVerbatim = atCount > 0;

        var hasEscape = false;

        while (true)
        {
            var surrogate = SlidingTextWindow.InvalidCharacter;
            var isEscaped = false;
            var character = TextWindow.PeekChar();

        top:
            switch (character)
            {
                case '\\':
                    if (!isEscaped && IsUnicodeEscape())
                    {
                        hasEscape = true;
                        isEscaped = true;
                        character = PeekUnicodeEscape(out surrogate);
                        goto top;
                    }

                    goto default;

                case SlidingTextWindow.InvalidCharacter:
                    if (!TextWindow.IsReallyAtEnd())
                    {
                        goto default;
                    }
                    goto LoopExit;

                case '_':
                case (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'):
                    break;

                case '0':
                    if (_identifierLength == 0)
                    {
                        goto LoopExit;
                    }

                    break;

                case >= '1' and <= '9':
                    if (_identifierLength == 0)
                    {
                        goto LoopExit;
                    }

                    break;

                case ' ':
                case '\t':
                case '.':
                case ';':
                case '(':
                case ')':
                case ',':
                    goto LoopExit;

                case '<':
                    // Not allowed in Akbura identifiers.
                    // https://github.com/dotnet/roslyn/blob/c27ea1941d547ca4b9263b0a5bd9b651d58d88b4/src/Compilers/CSharp/Portable/Parser/Lexer.cs#L1516
                    goto LoopExit;

                default:
                    if (_identifierLength == 0 && character > 127 && SyntaxFacts.IsIdentifierStartCharacter(character))
                    {
                        break;
                    }
                    else if (_identifierLength > 0 && character > 127 && SyntaxFacts.IsIdentifierPartCharacter(character))
                    {
                        if (UnicodeCharacterUtilities.IsFormattingChar(character))
                        {
                            //// BUG 424819 : Handle identifier chars > 0xFFFF via surrogate pairs
                            if (isEscaped)
                            {
                                NextCharOrUnicodeEscape(out surrogate, out var error);
                                AddError(error);
                            }
                            else
                            {
                                TextWindow.AdvanceChar();
                            }

                            continue; // Ignore formatting characters
                        }

                        break;
                    }

                    goto LoopExit;
            }

            if (isEscaped)
            {
                NextCharOrUnicodeEscape(out surrogate, out var err);
                AddError(err);
            }
            else
            {
                TextWindow.AdvanceChar();
            }

            AddIdentifierChar(character);

            if (surrogate != SlidingTextWindow.InvalidCharacter)
            {
                AddIdentifierChar(surrogate);
            }
        }

    LoopExit:
        var width = CurrentLexemeWidth;

        // id buffer is identical to width in input
        if (_identifierLength > 0)
        {
            info.Text = GetInternedLexemeText();

            if (_identifierLength == width)
            {
                info.StringValue = info.Text;
            }
            else
            {
                info.StringValue = TextWindow.Intern(_identifierBuffer, 0, _identifierLength);
            }

            info.HasIdentifierEscapeSequence = hasEscape;

            return true;
        }

        info.Text = null;
        info.StringValue = null;
        TextWindow.Reset(start);
        return false;
    }

    private void ResetIdentifierBuffer()
    {
        _identifierLength = 0;
    }

    private void AddIdentifierChar(char ch)
    {
        if (_identifierLength >= _identifierBuffer.Length)
        {
            GrowIdentifierBuffer();
        }

        _identifierBuffer[_identifierLength++] = ch;
    }

    private void GrowIdentifierBuffer()
    {
        var tmp = new char[_identifierBuffer.Length * 2];
        Array.Copy(_identifierBuffer, tmp, _identifierBuffer.Length);
        _identifierBuffer = tmp;
    }

    #endregion

    #region Unicode

    private bool IsUnicodeEscape()
    {
        if (TextWindow.PeekChar() == '\\')
        {
            var ch2 = TextWindow.PeekChar(1);
            if (ch2 == 'U' || ch2 == 'u')
            {
                return true;
            }
        }

        return false;
    }

    private char PeekCharOrUnicodeEscape(out char surrogateCharacter)
    {
        if (IsUnicodeEscape())
        {
            return PeekUnicodeEscape(out surrogateCharacter);
        }
        else
        {
            surrogateCharacter = SlidingTextWindow.InvalidCharacter;
            return TextWindow.PeekChar();
        }
    }

    private char PeekUnicodeEscape(out char surrogateCharacter)
    {
        var position = TextWindow.Position;

        // if we're peeking, then we don't want to change the position
        var ch = ScanUnicodeEscape(peek: true, surrogateCharacter: out surrogateCharacter, info: out var info);
        Debug.Assert(info == null, "Never produce a diagnostic while peeking.");
        TextWindow.Reset(position);
        return ch;
    }

    private char NextCharOrUnicodeEscape(out char surrogateCharacter, out AkburaDiagnostic? info)
    {
        var character = TextWindow.PeekChar();
        Debug.Assert(character != SlidingTextWindow.InvalidCharacter, "Precondition established by all callers; required for correctness of AdvanceChar() call.");
        if (character == '\\')
        {
            var ch2 = TextWindow.PeekChar(1);
            if (ch2 == 'U' || ch2 == 'u')
            {
                return ScanUnicodeEscape(peek: false, surrogateCharacter: out surrogateCharacter, info: out info);
            }
        }

        surrogateCharacter = SlidingTextWindow.InvalidCharacter;
        info = null;
        TextWindow.AdvanceChar();
        return character;
    }

    private char NextUnicodeEscape(out char surrogateCharacter, out AkburaDiagnostic? info)
    {
        return ScanUnicodeEscape(peek: false, surrogateCharacter: out surrogateCharacter, info: out info);
    }

    private char ScanUnicodeEscape(bool peek, out char surrogateCharacter, out AkburaDiagnostic? info)
    {
        surrogateCharacter = SlidingTextWindow.InvalidCharacter;
        info = null;

        var start = TextWindow.Position;
        var character = TextWindow.PeekChar();
        Debug.Assert(character == '\\');
        TextWindow.AdvanceChar();

        character = TextWindow.PeekChar();
        if (character == 'U')
        {
            uint uintChar = 0;

            TextWindow.AdvanceChar();
            if (!SyntaxFacts.IsHexDigit(TextWindow.PeekChar()))
            {
                if (!peek)
                {
                    info = CreateIllegalEscapeDiagnostic(start);
                }
            }
            else
            {
                for (var i = 0; i < 8; i++)
                {
                    character = TextWindow.PeekChar();
                    if (!SyntaxFacts.IsHexDigit(character))
                    {
                        if (!peek)
                        {
                            info = CreateIllegalEscapeDiagnostic(start);
                        }

                        break;
                    }

                    uintChar = (uint)((uintChar << 4) + SyntaxFacts.HexValue(character));
                    TextWindow.AdvanceChar();
                }

                if (uintChar > 0x0010FFFF)
                {
                    if (!peek)
                    {
                        info = CreateIllegalEscapeDiagnostic(start);
                    }
                }
                else
                {
                    character = GetCharsFromUtf32(uintChar, out surrogateCharacter);
                }
            }
        }
        else
        {
            Debug.Assert(character == 'u' || character == 'x');

            var intChar = 0;
            TextWindow.AdvanceChar();
            if (!SyntaxFacts.IsHexDigit(TextWindow.PeekChar()))
            {
                if (!peek)
                {
                    info = CreateIllegalEscapeDiagnostic(start);
                }
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    var ch2 = TextWindow.PeekChar();
                    if (!SyntaxFacts.IsHexDigit(ch2))
                    {
                        if (character == 'u')
                        {
                            if (!peek)
                            {
                                info = CreateIllegalEscapeDiagnostic(start);
                            }
                        }

                        break;
                    }

                    intChar = (intChar << 4) + SyntaxFacts.HexValue(ch2);
                    TextWindow.AdvanceChar();
                }

                character = (char)intChar;
            }
        }

        return character;
    }

    private static char GetCharsFromUtf32(uint codepoint, out char lowSurrogate)
    {
        if (codepoint < (uint)0x00010000)
        {
            lowSurrogate = SlidingTextWindow.InvalidCharacter;
            return (char)codepoint;
        }
        else
        {
            Debug.Assert(codepoint > 0x0000FFFF && codepoint <= 0x0010FFFF);
            lowSurrogate = (char)((codepoint - 0x00010000) % 0x0400 + 0xDC00);
            return (char)((codepoint - 0x00010000) / 0x0400 + 0xD800);
        }
    }

    #endregion

    #region Errors

    internal bool HasErrors => _errors != null;

    private void AddError(AkburaDiagnostic? error)
    {
        if (error != null)
        {
            _errors ??= new List<AkburaDiagnostic>(8);
            _errors.Add(error);
        }
    }

    private void AddError(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var diagnostic = new AkburaDiagnostic(
            parameters: [],
            code: code!,
            severity: AkburaDiagnosticSeverity.Error
        );

        AddError(diagnostic);
    }

    private void AddError(string? code, ImmutableArray<object?> parameters)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var diagnostic = new AkburaDiagnostic(
            parameters: parameters,
            code: code!,
            severity: AkburaDiagnosticSeverity.Error
        );

        AddError(diagnostic);
    }

    private void AddError(int position, int width, string? code, ImmutableArray<object?> parameters)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var diagnostic = new SyntaxDiagnosticInfo(
            position,
            width,
            parameters: parameters,
            code: code!,
            severity: AkburaDiagnosticSeverity.Error
        );

        AddError(diagnostic);
    }

    private ImmutableArray<AkburaDiagnostic> GetErrors()
    {
        if (_errors == null || _errors.Count == 0)
        {
            return [];
        }
        return [.. _errors];
    }

    /// <summary>
    /// Creates an error diagnostic for an illegal unicode escape sequence.
    /// </summary>
    private AkburaDiagnostic CreateIllegalEscapeDiagnostic(int start)
    {
        // Parameters convention:
        // index 0: absolute position in the source
        // index 1: width of the escape sequence
        //
        // You can use these in your resource messages via {0}, {1}, etc.

        var position = start;
        var width = TextWindow.Position - start;

        return new AkburaDiagnostic(
            parameters: [position, width],
            code: nameof(ErrorCodes.ERR_IllegalEscape),
            severity: AkburaDiagnosticSeverity.Error
        );
    }

    #endregion

    private static GreenSyntaxToken CreateToken(in TokenInfo tokenInfo, GreenSyntaxListBuilder? leading, GreenSyntaxListBuilder? trailing, ImmutableArray<AkburaDiagnostic>? diagnostics)
    {
        var leadingNode = leading?.ToListNode();
        var trailingNode = trailing?.ToListNode();

        GreenSyntaxToken? token = null;

        if (tokenInfo.Kind == SyntaxKind.CSharpRawToken)
        {
            AkburaDebug.AssertNotNull(tokenInfo.CSharpNode);

            // no leading, trailing trivia
            // also no diagnostics/annotations for now
            return GreenSyntaxToken.CreateCSharpRawToken(tokenInfo.CSharpNode);
        }

        if (tokenInfo.Kind == SyntaxKind.IdentifierToken)
        {
            AkburaDebug.AssertNotNull(tokenInfo.Text);

            token = GreenSyntaxToken.Identifier(leadingNode, tokenInfo.Text, trailingNode);
        }
        else if (tokenInfo.Kind == SyntaxKind.NumericLiteralToken)
        {
            AkburaDebug.AssertNotNull(tokenInfo.Text);
            switch (tokenInfo.ValueKind)
            {
                case SpecialType.System_Int32:
                    token = GreenSyntaxFactory.Literal(leadingNode, tokenInfo.Text, tokenInfo.IntValue, trailingNode);
                    break;
                case SpecialType.System_UInt32:
                    token = GreenSyntaxFactory.Literal(leadingNode, tokenInfo.Text, tokenInfo.UintValue, trailingNode);
                    break;
                case SpecialType.System_Int64:
                    token = GreenSyntaxFactory.Literal(leadingNode, tokenInfo.Text, tokenInfo.LongValue, trailingNode);
                    break;
                case SpecialType.System_UInt64:
                    token = GreenSyntaxFactory.Literal(leadingNode, tokenInfo.Text, tokenInfo.UlongValue, trailingNode);
                    break;
                case SpecialType.System_Single:
                    token = GreenSyntaxFactory.Literal(leadingNode, tokenInfo.Text, tokenInfo.FloatValue, trailingNode);
                    break;
                case SpecialType.System_Double:
                    token = GreenSyntaxFactory.Literal(leadingNode, tokenInfo.Text, tokenInfo.DoubleValue, trailingNode);
                    break;
                case SpecialType.System_Decimal:
                    token = GreenSyntaxFactory.Literal(leadingNode, tokenInfo.Text, tokenInfo.DecimalValue, trailingNode);
                    break;
                default:
                    ThrowHelper.UnexpectedValue(tokenInfo.ValueKind);
                    break;
            }
        }
        else
        {
            token = GreenSyntaxFactory.Token(leadingNode, tokenInfo.Kind, trailingNode);
        }

        AkburaDebug.AssertNotNull(token);

        if (diagnostics?.IsDefaultOrEmpty == false)
        {
            token = Unsafe.As<GreenSyntaxToken>(token.WithDiagnostics(diagnostics));
        }

        return token;
    }
}
#pragma warning restore RSEXPERIMENTAL003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.