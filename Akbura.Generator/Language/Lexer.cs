using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CodeAnalysisSyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;
using Microsoft.CodeAnalysis;
using System.Diagnostics;
using System.Collections.Immutable;

namespace Akbura.Language;

#pragma warning disable RSEXPERIMENTAL003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
internal sealed partial class Lexer : IDisposable
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

    private SyntaxListBuilder _leadingTriviaCache;
    private SyntaxListBuilder _trailingTriviaCache;

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
            _ => ParseNextToken()
        };

        return CreateToken(in tokenInfo);
    }

    private TokenInfo ParseNextToken()
    {
        return default;
    }

    public void Dispose()
    {
        this.TextWindow.Free();
    }

    private void Start()
    {
        LexemeStartPosition = this.TextWindow.Position;
        _errors = null;
    }

    internal int LexemeStartPosition;

    internal int CurrentLexemeWidth => this.TextWindow.Position - LexemeStartPosition;

    internal string GetInternedLexemeText()
            => TextWindow.GetText(LexemeStartPosition, intern: true);

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
        ResetIdentBuffer();

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

            info.IsVerbatim = false;
            info.HasIdentifierEscapeSequence = hasEscape;

            return true;
        }

        info.Text = null;
        info.StringValue = null;
        TextWindow.Reset(start);
        return false;
    }

    private void ResetIdentBuffer()
    {
        _identifierLength = 0;
    }

    private void AddIdentifierChar(char ch)
    {
        if (_identifierLength >= _identifierBuffer.Length)
        {
            GrowIdentBuffer();
        }

        _identifierBuffer[_identifierLength++] = ch;
    }

    private void GrowIdentBuffer()
    {
        var tmp = new char[_identifierBuffer.Length * 2];
        Array.Copy(_identifierBuffer, tmp, _identifierBuffer.Length);
        _identifierBuffer = tmp;
    }

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

    private void AddError(AkburaDiagnostic? error)
    {
        if (error != null)
        {
            _errors ??= new List<AkburaDiagnostic>(8);
            _errors.Add(error);
        }
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

    private static GreenSyntaxToken CreateToken(in TokenInfo tokenInfo)
    {
        if (tokenInfo.Kind == SyntaxKind.CSharpRawToken)
        {
            AkburaDebug.AssertNotNull(tokenInfo.CSharpNode);

            return GreenSyntaxToken.CreateCSharpRawToken(tokenInfo.CSharpNode);
        }

        if (tokenInfo.Kind == SyntaxKind.IdentifierToken)
        {
            AkburaDebug.AssertNotNull(tokenInfo.Text);

            return GreenSyntaxToken.Identifier(tokenInfo.Text);
        }

        throw new NotImplementedException();
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
}
#pragma warning restore RSEXPERIMENTAL003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.