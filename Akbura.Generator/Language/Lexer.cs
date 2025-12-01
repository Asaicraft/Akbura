// THis file is ported and adopted from roslyn

using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CodeAnalysisSyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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

        if(mode != LexerMode.TopLevel)
        {
            var tokenInfo = mode switch
            {
                LexerMode.InInlineExpression => ParseInlineExpression(),
                LexerMode.InExpressionUntilSemicolon => ParseExpressionUntilSemicolon(),
                LexerMode.InExpressionUntilComma => ParseExpressionUntilComma(),
                _ => default
            };

            // In expression modes, we do not care about trivia or errors.
            return CreateToken(in tokenInfo, null, null, null);
        }

        return ParseNextToken();
    }

    private GreenSyntaxToken ParseNextToken()
    {
        _leadingTriviaCache.Clear();
        LexSyntaxTrivia(isTrailing: false, triviaList: ref _leadingTriviaCache);
        var leading = _leadingTriviaCache;

        TokenInfo tokenInfo = default;

        Start();
        var errors = GetErrors();

        _trailingTriviaCache.Clear();
        LexSyntaxTrivia(isTrailing: true, triviaList: ref _trailingTriviaCache);
        var trailing = _trailingTriviaCache;

        return CreateToken(in tokenInfo, leading, trailing, errors);
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

    internal string GetNonInternedLexemeText()
            => TextWindow.GetText(LexemeStartPosition, intern: false);

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

    private ImmutableArray<AkburaDiagnostic> GetErrors()
    {
        if (_errors == null || _errors.Count == 0)
        {
            return [];
        }
        return ImmutableArray.CreateRange(_errors);
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

        AkburaDebug.AssertNotNull(token);

        if (diagnostics?.IsDefaultOrEmpty == false)
        {
            token = Unsafe.As<GreenSyntaxToken>(token.WithDiagnostics(diagnostics));
        }

        return token;
    }
}
#pragma warning restore RSEXPERIMENTAL003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.