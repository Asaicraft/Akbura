using Akbura.Language.Syntax.Green;
using System;
using System.Diagnostics;

namespace Akbura.Language;

internal sealed partial class Lexer
{
    // Roslyn-style quick scanner: one table classifies chars, another table moves
    // a compact DFA. The hot path only computes full width + FNV hash; real token
    // creation is delegated to the regular lexer on cache miss.
    private enum QuickScanState : byte
    {
        Initial,
        FollowingWhite,
        FollowingCR,
        Ident,
        Number,
        Punctuation,
        Dot,
        Slash,
        Bang,
        Colon,
        Less,
        Equals,
        Greater,
        DoneAfterNext,
        // Bad must immediately follow Done so a single "state >= Done" check exits.
        Done,
        Bad = Done + 1
    }

    private enum CharFlags : byte
    {
        White,
        CR,
        LF,
        Letter,
        Digit,
        Punct,
        Dot,
        Slash,
        Asterisk,
        Bang,
        Colon,
        Less,
        Equals,
        Greater,
        Complex,
        EndOfFile
    }

    private const int CharFlagsCount = (int)CharFlags.EndOfFile + 1;
    private const int CharFlagsShift = 4;

    // Flat DFA table. Rows are QuickScanState values, columns are CharFlags values.
    // Keep the order in sync with QuickScanState and CharFlags; CharFlagsCount must stay 16.
    //
    // Columns: White, CR, LF, Letter, Digit, Punct, Dot, Slash,
    //          Asterisk, Bang, Colon, Less, Equals, Greater, Complex, EndOfFile.
    private static ReadOnlySpan<byte> StateTransitions =>
    [
        //  0: Initial - no token yet
        (byte)QuickScanState.Initial,        //  0: White
        (byte)QuickScanState.Initial,        //  1: CR
        (byte)QuickScanState.Initial,        //  2: LF
        (byte)QuickScanState.Ident,          //  3: Letter
        (byte)QuickScanState.Number,         //  4: Digit
        (byte)QuickScanState.Punctuation,    //  5: Punct
        (byte)QuickScanState.Dot,            //  6: Dot
        (byte)QuickScanState.Slash,          //  7: Slash
        (byte)QuickScanState.Punctuation,    //  8: Asterisk
        (byte)QuickScanState.Bang,           //  9: Bang
        (byte)QuickScanState.Colon,          // 10: Colon
        (byte)QuickScanState.Less,           // 11: Less
        (byte)QuickScanState.Equals,         // 12: Equals
        (byte)QuickScanState.Greater,        // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Bad,            // 15: EndOfFile

        //  1: FollowingWhite - currently scanning simple whitespace
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        //  2: FollowingCR - just saw '\r'; accept a following '\n'
        (byte)QuickScanState.Done,           //  0: White
        (byte)QuickScanState.Done,           //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Done,           //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Done,           // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        //  3: Ident - identifier body: letters and digits are still part of it
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Ident,          //  3: Letter
        (byte)QuickScanState.Ident,          //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        //  4: Number - decimal digits only; anything more complex falls back
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Bad,            //  3: Letter
        (byte)QuickScanState.Number,         //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Bad,            //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        //  5: Punctuation - single-character simple punctuation
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        //  6: Dot - '.' may become '..' or part of a number, so it has its own row
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Bad,            //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Punctuation,    //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        //  7: Slash - '/' is separate so comments and '/=' can fall back when needed
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Bad,            //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Punctuation,    // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        //  8: Bang - '!' is separate so '!=' can be accepted
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Punctuation,    // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        //  9: Colon - ':' is separate so '::' can be accepted
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Punctuation,    // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        // 10: Less - '<' is separate so '<=' and '</' can be accepted
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Punctuation,    //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Punctuation,    // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        // 11: Equals - '=' is separate so '==' and '=>' can be accepted
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Punctuation,    // 12: Equals
        (byte)QuickScanState.Punctuation,    // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        // 12: Greater - '>' is separate so '>=' can be accepted
        (byte)QuickScanState.FollowingWhite, //  0: White
        (byte)QuickScanState.FollowingCR,    //  1: CR
        (byte)QuickScanState.DoneAfterNext,  //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Bad,            //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Punctuation,    // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Bad,            // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile

        // 13: DoneAfterNext - consume one more char, then finish
        (byte)QuickScanState.Done,           //  0: White
        (byte)QuickScanState.Done,           //  1: CR
        (byte)QuickScanState.Done,           //  2: LF
        (byte)QuickScanState.Done,           //  3: Letter
        (byte)QuickScanState.Done,           //  4: Digit
        (byte)QuickScanState.Done,           //  5: Punct
        (byte)QuickScanState.Done,           //  6: Dot
        (byte)QuickScanState.Done,           //  7: Slash
        (byte)QuickScanState.Done,           //  8: Asterisk
        (byte)QuickScanState.Done,           //  9: Bang
        (byte)QuickScanState.Done,           // 10: Colon
        (byte)QuickScanState.Done,           // 11: Less
        (byte)QuickScanState.Done,           // 12: Equals
        (byte)QuickScanState.Done,           // 13: Greater
        (byte)QuickScanState.Done,           // 14: Complex
        (byte)QuickScanState.Done,           // 15: EndOfFile
    ];

    private bool TryQuickScanToken(LexerMode mode, out GreenSyntaxToken token)
    {
        var position = TextWindow.Position;

        if (TryQuickScanTokenCore(mode, out token))
        {
#if STATS
            RecordQuickScannerHit();
#endif
            return true;
        }

        Debug.Assert(TextWindow.Position == position, "Quick scanner fallback must not consume text.");
#if STATS
        RecordQuickScannerFallback();
#endif
        token = null!;
        return false;
    }

    private bool TryQuickScanTokenCore(LexerMode mode, out GreenSyntaxToken token)
    {
        token = null!;
        Start();

        var textWindowCharSpan = TextWindow.CurrentWindowSpan;
        if (textWindowCharSpan.IsEmpty)
        {
            return false;
        }

        if (mode == LexerMode.TopLevel &&
            ShouldFallbackForTopLevelAtSign(textWindowCharSpan))
        {
            return false;
        }

        // Cap how much of the char span we're willing to look at.
        textWindowCharSpan = textWindowCharSpan[..Math.Min(MaxCachedTokenSize, textWindowCharSpan.Length)];

        var charProperties = CharProperties;
        var charPropertiesLength = Math.Min(128, charProperties.Length);
        var stateTransitions = StateTransitions;

        Debug.Assert(CharFlagsCount == 1 << CharFlagsShift);

        var state = QuickScanState.Initial;
        var hashCode = HashCode.FnvOffsetBias;

        var currentIndex = 0;
        for (; currentIndex < textWindowCharSpan.Length; currentIndex++)
        {
            var c = textWindowCharSpan[currentIndex];
            var uc = unchecked((int)c);
            var flags = (uint)uc < (uint)charPropertiesLength
                ? (CharFlags)charProperties[uc]
                : CharFlags.Complex;

            state = (QuickScanState)stateTransitions[((int)state << CharFlagsShift) + (int)flags];

            // Bad > Done and it is the only state like that.
            // As a result, we exit the loop on either Bad or Done.
            if (state >= QuickScanState.Done)
            {
                goto exitLoop;
            }

            hashCode = HashCode.CombineFNVHash(hashCode, c);
        }

        // We reached the end of the current span without seeing a terminator.
        // Roslyn normally observes an EOF sentinel in the window. This text window
        // can end exactly at real EOF, so synthesize that transition without
        // hashing or consuming an EOF character. If this is only a window /
        // MaxCachedTokenSize boundary, keep the conservative fallback.
        if (TextWindow.Position + currentIndex >= TextWindow.Text.Length)
        {
            state = (QuickScanState)stateTransitions[((int)state << CharFlagsShift) + (int)CharFlags.EndOfFile];
        }
        else
        {
            state = QuickScanState.Bad;
        }

    exitLoop:
        Debug.Assert(state == QuickScanState.Bad || state == QuickScanState.Done, "can only exit with Bad or Done");

        if (state != QuickScanState.Done)
        {
            return false;
        }

        var tokenLength = currentIndex;
        if (tokenLength <= 0 || tokenLength > MaxCachedTokenSize)
        {
            return false;
        }

        TextWindow.AdvanceChar(tokenLength);

        token = _cache.LookupToken(
            textWindowCharSpan[..tokenLength],
            hashCode,
            static lexer => CreateQuickTokenFromRegularLexer(lexer),
            this);

        return true;
    }

    private bool ShouldFallbackForTopLevelAtSign(ReadOnlySpan<char> textWindowCharSpan)
    {
        var currentIndex = 0;

        while (currentIndex < textWindowCharSpan.Length)
        {
            switch (textWindowCharSpan[currentIndex])
            {
                case ' ':
                case '\t':
                case '\v':
                case '\f':
                case '\u001A':
                    currentIndex++;
                    continue;

                case '\r':
                    currentIndex++;
                    if (currentIndex < textWindowCharSpan.Length &&
                        textWindowCharSpan[currentIndex] == '\n')
                    {
                        currentIndex++;
                    }

                    continue;

                case '\n':
                    currentIndex++;
                    continue;
            }

            break;
        }

        if (currentIndex >= textWindowCharSpan.Length ||
            textWindowCharSpan[currentIndex] != '@')
        {
            return false;
        }

        // Keep the hot path for the common file-level form: @akcss { ... }.
        // When @ is behind leading trivia, the regular lexer remains the source
        // of truth because IsAkcssDirectiveStart() checks the current window
        // position directly.
        return currentIndex != 0 || !IsAkcssDirectiveStart();
    }

    private static GreenSyntaxToken CreateQuickTokenFromRegularLexer(Lexer lexer)
    {
#if DEBUG
        var expectedFullWidth = lexer.CurrentLexemeWidth;
#endif
        var fullTokenStart = lexer.LexemeStartPosition;

        lexer.TextWindow.Reset(fullTokenStart);
        var token = lexer.ParseNextToken();

#if DEBUG
        Debug.Assert(token.FullWidth == expectedFullWidth);
        Debug.Assert(lexer.TextWindow.Position - fullTokenStart == expectedFullWidth);
#endif
        return token;
    }

    // Character classification table for U+0000..U+017F.
    // Roslyn keeps this kind of table readable by grouping code points and
    // annotating the important ASCII entries. Keep indices in exact code-point order.
    //
    // NOTE: TryQuickScanTokenCore currently caps lookup to 128 characters:
    //     var charPropertiesLength = Math.Min(128, charProperties.Length);
    // Entries U+0080..U+017F are documented here, but are not used until that cap is removed.
    private static ReadOnlySpan<byte> CharProperties =>
    [
        // U+0000 .. U+001F
        (byte)CharFlags.Complex,  //   0 / U+0000 NUL
        (byte)CharFlags.Complex,  //   1 / U+0001 SOH
        (byte)CharFlags.Complex,  //   2 / U+0002 STX
        (byte)CharFlags.Complex,  //   3 / U+0003 ETX
        (byte)CharFlags.Complex,  //   4 / U+0004 EOT
        (byte)CharFlags.Complex,  //   5 / U+0005 ENQ
        (byte)CharFlags.Complex,  //   6 / U+0006 ACK
        (byte)CharFlags.Complex,  //   7 / U+0007 BEL
        (byte)CharFlags.Complex,  //   8 / U+0008 BS
        (byte)CharFlags.White,    //   9 / U+0009 TAB
        (byte)CharFlags.LF,       //  10 / U+000A LF
        (byte)CharFlags.White,    //  11 / U+000B VT
        (byte)CharFlags.White,    //  12 / U+000C FF
        (byte)CharFlags.CR,       //  13 / U+000D CR
        (byte)CharFlags.Complex,  //  14 / U+000E SO
        (byte)CharFlags.Complex,  //  15 / U+000F SI
        (byte)CharFlags.Complex,  //  16 / U+0010 DLE
        (byte)CharFlags.Complex,  //  17 / U+0011 DC1
        (byte)CharFlags.Complex,  //  18 / U+0012 DC2
        (byte)CharFlags.Complex,  //  19 / U+0013 DC3
        (byte)CharFlags.Complex,  //  20 / U+0014 DC4
        (byte)CharFlags.Complex,  //  21 / U+0015 NAK
        (byte)CharFlags.Complex,  //  22 / U+0016 SYN
        (byte)CharFlags.Complex,  //  23 / U+0017 ETB
        (byte)CharFlags.Complex,  //  24 / U+0018 CAN
        (byte)CharFlags.Complex,  //  25 / U+0019 EM
        (byte)CharFlags.White,    //  26 / U+001A SUB / Ctrl+Z
        (byte)CharFlags.Complex,  //  27 / U+001B ESC
        (byte)CharFlags.Complex,  //  28 / U+001C FS
        (byte)CharFlags.Complex,  //  29 / U+001D GS
        (byte)CharFlags.Complex,  //  30 / U+001E RS
        (byte)CharFlags.Complex,  //  31 / U+001F US

        // U+0020 .. U+003F
        (byte)CharFlags.White,    //  32 / U+0020 SPC
        (byte)CharFlags.Bang,     //  33 / U+0021 '!'
        (byte)CharFlags.Punct,    //  34 / U+0022 '"'
        (byte)CharFlags.Complex,  //  35 / U+0023 '#'
        (byte)CharFlags.Complex,  //  36 / U+0024 '$'
        (byte)CharFlags.Punct,    //  37 / U+0025 '%'
        (byte)CharFlags.Punct,    //  38 / U+0026 '&'
        (byte)CharFlags.Punct,    //  39 / U+0027 APOSTROPHE
        (byte)CharFlags.Punct,    //  40 / U+0028 '('
        (byte)CharFlags.Punct,    //  41 / U+0029 ')'
        (byte)CharFlags.Asterisk, //  42 / U+002A '*'
        (byte)CharFlags.Punct,    //  43 / U+002B '+'
        (byte)CharFlags.Punct,    //  44 / U+002C ','
        (byte)CharFlags.Punct,    //  45 / U+002D '-'
        (byte)CharFlags.Dot,      //  46 / U+002E '.'
        (byte)CharFlags.Slash,    //  47 / U+002F '/'
        (byte)CharFlags.Digit,    //  48 / U+0030 '0'
        (byte)CharFlags.Digit,    //  49 / U+0031 '1'
        (byte)CharFlags.Digit,    //  50 / U+0032 '2'
        (byte)CharFlags.Digit,    //  51 / U+0033 '3'
        (byte)CharFlags.Digit,    //  52 / U+0034 '4'
        (byte)CharFlags.Digit,    //  53 / U+0035 '5'
        (byte)CharFlags.Digit,    //  54 / U+0036 '6'
        (byte)CharFlags.Digit,    //  55 / U+0037 '7'
        (byte)CharFlags.Digit,    //  56 / U+0038 '8'
        (byte)CharFlags.Digit,    //  57 / U+0039 '9'
        (byte)CharFlags.Colon,    //  58 / U+003A ':'
        (byte)CharFlags.Punct,    //  59 / U+003B ';'
        (byte)CharFlags.Less,     //  60 / U+003C '<'
        (byte)CharFlags.Equals,   //  61 / U+003D '='
        (byte)CharFlags.Greater,  //  62 / U+003E '>'
        (byte)CharFlags.Punct,    //  63 / U+003F '?'

        // U+0040 .. U+005F
        (byte)CharFlags.Punct,    //  64 / U+0040 '@'
        (byte)CharFlags.Letter,   //  65 / U+0041 'A'
        (byte)CharFlags.Letter,   //  66 / U+0042 'B'
        (byte)CharFlags.Letter,   //  67 / U+0043 'C'
        (byte)CharFlags.Letter,   //  68 / U+0044 'D'
        (byte)CharFlags.Letter,   //  69 / U+0045 'E'
        (byte)CharFlags.Letter,   //  70 / U+0046 'F'
        (byte)CharFlags.Letter,   //  71 / U+0047 'G'
        (byte)CharFlags.Letter,   //  72 / U+0048 'H'
        (byte)CharFlags.Letter,   //  73 / U+0049 'I'
        (byte)CharFlags.Letter,   //  74 / U+004A 'J'
        (byte)CharFlags.Letter,   //  75 / U+004B 'K'
        (byte)CharFlags.Letter,   //  76 / U+004C 'L'
        (byte)CharFlags.Letter,   //  77 / U+004D 'M'
        (byte)CharFlags.Letter,   //  78 / U+004E 'N'
        (byte)CharFlags.Letter,   //  79 / U+004F 'O'
        (byte)CharFlags.Letter,   //  80 / U+0050 'P'
        (byte)CharFlags.Letter,   //  81 / U+0051 'Q'
        (byte)CharFlags.Letter,   //  82 / U+0052 'R'
        (byte)CharFlags.Letter,   //  83 / U+0053 'S'
        (byte)CharFlags.Letter,   //  84 / U+0054 'T'
        (byte)CharFlags.Letter,   //  85 / U+0055 'U'
        (byte)CharFlags.Letter,   //  86 / U+0056 'V'
        (byte)CharFlags.Letter,   //  87 / U+0057 'W'
        (byte)CharFlags.Letter,   //  88 / U+0058 'X'
        (byte)CharFlags.Letter,   //  89 / U+0059 'Y'
        (byte)CharFlags.Letter,   //  90 / U+005A 'Z'
        (byte)CharFlags.Punct,    //  91 / U+005B '['
        (byte)CharFlags.Complex,  //  92 / U+005C '\\'
        (byte)CharFlags.Punct,    //  93 / U+005D ']'
        (byte)CharFlags.Punct,    //  94 / U+005E '^'
        (byte)CharFlags.Letter,   //  95 / U+005F '_'

        // U+0060 .. U+007F
        (byte)CharFlags.Complex,  //  96 / U+0060 '`'
        (byte)CharFlags.Letter,   //  97 / U+0061 'a'
        (byte)CharFlags.Letter,   //  98 / U+0062 'b'
        (byte)CharFlags.Letter,   //  99 / U+0063 'c'
        (byte)CharFlags.Letter,   // 100 / U+0064 'd'
        (byte)CharFlags.Letter,   // 101 / U+0065 'e'
        (byte)CharFlags.Letter,   // 102 / U+0066 'f'
        (byte)CharFlags.Letter,   // 103 / U+0067 'g'
        (byte)CharFlags.Letter,   // 104 / U+0068 'h'
        (byte)CharFlags.Letter,   // 105 / U+0069 'i'
        (byte)CharFlags.Letter,   // 106 / U+006A 'j'
        (byte)CharFlags.Letter,   // 107 / U+006B 'k'
        (byte)CharFlags.Letter,   // 108 / U+006C 'l'
        (byte)CharFlags.Letter,   // 109 / U+006D 'm'
        (byte)CharFlags.Letter,   // 110 / U+006E 'n'
        (byte)CharFlags.Letter,   // 111 / U+006F 'o'
        (byte)CharFlags.Letter,   // 112 / U+0070 'p'
        (byte)CharFlags.Letter,   // 113 / U+0071 'q'
        (byte)CharFlags.Letter,   // 114 / U+0072 'r'
        (byte)CharFlags.Letter,   // 115 / U+0073 's'
        (byte)CharFlags.Letter,   // 116 / U+0074 't'
        (byte)CharFlags.Letter,   // 117 / U+0075 'u'
        (byte)CharFlags.Letter,   // 118 / U+0076 'v'
        (byte)CharFlags.Letter,   // 119 / U+0077 'w'
        (byte)CharFlags.Letter,   // 120 / U+0078 'x'
        (byte)CharFlags.Letter,   // 121 / U+0079 'y'
        (byte)CharFlags.Letter,   // 122 / U+007A 'z'
        (byte)CharFlags.Punct,    // 123 / U+007B '{'
        (byte)CharFlags.Punct,    // 124 / U+007C '|'
        (byte)CharFlags.Punct,    // 125 / U+007D '}'
        (byte)CharFlags.Punct,    // 126 / U+007E '~'
        (byte)CharFlags.Complex,  // 127 / U+007F DEL

        // U+0080 .. U+009F
        (byte)CharFlags.Complex,  // 128 / U+0080
        (byte)CharFlags.Complex,  // 129 / U+0081
        (byte)CharFlags.Complex,  // 130 / U+0082
        (byte)CharFlags.Complex,  // 131 / U+0083
        (byte)CharFlags.Complex,  // 132 / U+0084
        (byte)CharFlags.Complex,  // 133 / U+0085
        (byte)CharFlags.Complex,  // 134 / U+0086
        (byte)CharFlags.Complex,  // 135 / U+0087
        (byte)CharFlags.Complex,  // 136 / U+0088
        (byte)CharFlags.Complex,  // 137 / U+0089
        (byte)CharFlags.Complex,  // 138 / U+008A
        (byte)CharFlags.Complex,  // 139 / U+008B
        (byte)CharFlags.Complex,  // 140 / U+008C
        (byte)CharFlags.Complex,  // 141 / U+008D
        (byte)CharFlags.Complex,  // 142 / U+008E
        (byte)CharFlags.Complex,  // 143 / U+008F
        (byte)CharFlags.Complex,  // 144 / U+0090
        (byte)CharFlags.Complex,  // 145 / U+0091
        (byte)CharFlags.Complex,  // 146 / U+0092
        (byte)CharFlags.Complex,  // 147 / U+0093
        (byte)CharFlags.Complex,  // 148 / U+0094
        (byte)CharFlags.Complex,  // 149 / U+0095
        (byte)CharFlags.Complex,  // 150 / U+0096
        (byte)CharFlags.Complex,  // 151 / U+0097
        (byte)CharFlags.Complex,  // 152 / U+0098
        (byte)CharFlags.Complex,  // 153 / U+0099
        (byte)CharFlags.Complex,  // 154 / U+009A
        (byte)CharFlags.Complex,  // 155 / U+009B
        (byte)CharFlags.Complex,  // 156 / U+009C
        (byte)CharFlags.Complex,  // 157 / U+009D
        (byte)CharFlags.Complex,  // 158 / U+009E
        (byte)CharFlags.Complex,  // 159 / U+009F

        // U+00A0 .. U+00BF
        (byte)CharFlags.Complex,  // 160 / U+00A0 NO-BREAK SPACE
        (byte)CharFlags.Complex,  // 161 / U+00A1 INVERTED EXCLAMATION MARK
        (byte)CharFlags.Complex,  // 162 / U+00A2 CENT SIGN
        (byte)CharFlags.Complex,  // 163 / U+00A3 POUND SIGN
        (byte)CharFlags.Complex,  // 164 / U+00A4 CURRENCY SIGN
        (byte)CharFlags.Complex,  // 165 / U+00A5 YEN SIGN
        (byte)CharFlags.Complex,  // 166 / U+00A6 BROKEN BAR
        (byte)CharFlags.Complex,  // 167 / U+00A7 SECTION SIGN
        (byte)CharFlags.Complex,  // 168 / U+00A8 DIAERESIS
        (byte)CharFlags.Complex,  // 169 / U+00A9 COPYRIGHT SIGN
        (byte)CharFlags.Letter,   // 170 / U+00AA FEMININE ORDINAL INDICATOR
        (byte)CharFlags.Complex,  // 171 / U+00AB LEFT-POINTING DOUBLE ANGLE QUOTATION MARK
        (byte)CharFlags.Complex,  // 172 / U+00AC NOT SIGN
        (byte)CharFlags.Complex,  // 173 / U+00AD SOFT HYPHEN
        (byte)CharFlags.Complex,  // 174 / U+00AE REGISTERED SIGN
        (byte)CharFlags.Complex,  // 175 / U+00AF MACRON
        (byte)CharFlags.Complex,  // 176 / U+00B0 DEGREE SIGN
        (byte)CharFlags.Complex,  // 177 / U+00B1 PLUS-MINUS SIGN
        (byte)CharFlags.Complex,  // 178 / U+00B2 SUPERSCRIPT TWO
        (byte)CharFlags.Complex,  // 179 / U+00B3 SUPERSCRIPT THREE
        (byte)CharFlags.Complex,  // 180 / U+00B4 ACUTE ACCENT
        (byte)CharFlags.Letter,   // 181 / U+00B5 MICRO SIGN
        (byte)CharFlags.Complex,  // 182 / U+00B6 PILCROW SIGN
        (byte)CharFlags.Complex,  // 183 / U+00B7 MIDDLE DOT
        (byte)CharFlags.Complex,  // 184 / U+00B8 CEDILLA
        (byte)CharFlags.Complex,  // 185 / U+00B9 SUPERSCRIPT ONE
        (byte)CharFlags.Letter,   // 186 / U+00BA MASCULINE ORDINAL INDICATOR
        (byte)CharFlags.Complex,  // 187 / U+00BB RIGHT-POINTING DOUBLE ANGLE QUOTATION MARK
        (byte)CharFlags.Complex,  // 188 / U+00BC VULGAR FRACTION ONE QUARTER
        (byte)CharFlags.Complex,  // 189 / U+00BD VULGAR FRACTION ONE HALF
        (byte)CharFlags.Complex,  // 190 / U+00BE VULGAR FRACTION THREE QUARTERS
        (byte)CharFlags.Complex,  // 191 / U+00BF INVERTED QUESTION MARK

        // U+00C0 .. U+00DF
        (byte)CharFlags.Letter,   // 192 / U+00C0
        (byte)CharFlags.Letter,   // 193 / U+00C1
        (byte)CharFlags.Letter,   // 194 / U+00C2
        (byte)CharFlags.Letter,   // 195 / U+00C3
        (byte)CharFlags.Letter,   // 196 / U+00C4
        (byte)CharFlags.Letter,   // 197 / U+00C5
        (byte)CharFlags.Letter,   // 198 / U+00C6
        (byte)CharFlags.Letter,   // 199 / U+00C7
        (byte)CharFlags.Letter,   // 200 / U+00C8
        (byte)CharFlags.Letter,   // 201 / U+00C9
        (byte)CharFlags.Letter,   // 202 / U+00CA
        (byte)CharFlags.Letter,   // 203 / U+00CB
        (byte)CharFlags.Letter,   // 204 / U+00CC
        (byte)CharFlags.Letter,   // 205 / U+00CD
        (byte)CharFlags.Letter,   // 206 / U+00CE
        (byte)CharFlags.Letter,   // 207 / U+00CF
        (byte)CharFlags.Letter,   // 208 / U+00D0
        (byte)CharFlags.Letter,   // 209 / U+00D1
        (byte)CharFlags.Letter,   // 210 / U+00D2
        (byte)CharFlags.Letter,   // 211 / U+00D3
        (byte)CharFlags.Letter,   // 212 / U+00D4
        (byte)CharFlags.Letter,   // 213 / U+00D5
        (byte)CharFlags.Letter,   // 214 / U+00D6
        (byte)CharFlags.Complex,  // 215 / U+00D7 MULTIPLICATION SIGN
        (byte)CharFlags.Letter,   // 216 / U+00D8
        (byte)CharFlags.Letter,   // 217 / U+00D9
        (byte)CharFlags.Letter,   // 218 / U+00DA
        (byte)CharFlags.Letter,   // 219 / U+00DB
        (byte)CharFlags.Letter,   // 220 / U+00DC
        (byte)CharFlags.Letter,   // 221 / U+00DD
        (byte)CharFlags.Letter,   // 222 / U+00DE
        (byte)CharFlags.Letter,   // 223 / U+00DF

        // U+00E0 .. U+00FF
        (byte)CharFlags.Letter,   // 224 / U+00E0
        (byte)CharFlags.Letter,   // 225 / U+00E1
        (byte)CharFlags.Letter,   // 226 / U+00E2
        (byte)CharFlags.Letter,   // 227 / U+00E3
        (byte)CharFlags.Letter,   // 228 / U+00E4
        (byte)CharFlags.Letter,   // 229 / U+00E5
        (byte)CharFlags.Letter,   // 230 / U+00E6
        (byte)CharFlags.Letter,   // 231 / U+00E7
        (byte)CharFlags.Letter,   // 232 / U+00E8
        (byte)CharFlags.Letter,   // 233 / U+00E9
        (byte)CharFlags.Letter,   // 234 / U+00EA
        (byte)CharFlags.Letter,   // 235 / U+00EB
        (byte)CharFlags.Letter,   // 236 / U+00EC
        (byte)CharFlags.Letter,   // 237 / U+00ED
        (byte)CharFlags.Letter,   // 238 / U+00EE
        (byte)CharFlags.Letter,   // 239 / U+00EF
        (byte)CharFlags.Letter,   // 240 / U+00F0
        (byte)CharFlags.Letter,   // 241 / U+00F1
        (byte)CharFlags.Letter,   // 242 / U+00F2
        (byte)CharFlags.Letter,   // 243 / U+00F3
        (byte)CharFlags.Letter,   // 244 / U+00F4
        (byte)CharFlags.Letter,   // 245 / U+00F5
        (byte)CharFlags.Letter,   // 246 / U+00F6
        (byte)CharFlags.Complex,  // 247 / U+00F7 DIVISION SIGN
        (byte)CharFlags.Letter,   // 248 / U+00F8
        (byte)CharFlags.Letter,   // 249 / U+00F9
        (byte)CharFlags.Letter,   // 250 / U+00FA
        (byte)CharFlags.Letter,   // 251 / U+00FB
        (byte)CharFlags.Letter,   // 252 / U+00FC
        (byte)CharFlags.Letter,   // 253 / U+00FD
        (byte)CharFlags.Letter,   // 254 / U+00FE
        (byte)CharFlags.Letter,   // 255 / U+00FF

        // U+0100 .. U+011F
        (byte)CharFlags.Letter,   // 256 / U+0100
        (byte)CharFlags.Letter,   // 257 / U+0101
        (byte)CharFlags.Letter,   // 258 / U+0102
        (byte)CharFlags.Letter,   // 259 / U+0103
        (byte)CharFlags.Letter,   // 260 / U+0104
        (byte)CharFlags.Letter,   // 261 / U+0105
        (byte)CharFlags.Letter,   // 262 / U+0106
        (byte)CharFlags.Letter,   // 263 / U+0107
        (byte)CharFlags.Letter,   // 264 / U+0108
        (byte)CharFlags.Letter,   // 265 / U+0109
        (byte)CharFlags.Letter,   // 266 / U+010A
        (byte)CharFlags.Letter,   // 267 / U+010B
        (byte)CharFlags.Letter,   // 268 / U+010C
        (byte)CharFlags.Letter,   // 269 / U+010D
        (byte)CharFlags.Letter,   // 270 / U+010E
        (byte)CharFlags.Letter,   // 271 / U+010F
        (byte)CharFlags.Letter,   // 272 / U+0110
        (byte)CharFlags.Letter,   // 273 / U+0111
        (byte)CharFlags.Letter,   // 274 / U+0112
        (byte)CharFlags.Letter,   // 275 / U+0113
        (byte)CharFlags.Letter,   // 276 / U+0114
        (byte)CharFlags.Letter,   // 277 / U+0115
        (byte)CharFlags.Letter,   // 278 / U+0116
        (byte)CharFlags.Letter,   // 279 / U+0117
        (byte)CharFlags.Letter,   // 280 / U+0118
        (byte)CharFlags.Letter,   // 281 / U+0119
        (byte)CharFlags.Letter,   // 282 / U+011A
        (byte)CharFlags.Letter,   // 283 / U+011B
        (byte)CharFlags.Letter,   // 284 / U+011C
        (byte)CharFlags.Letter,   // 285 / U+011D
        (byte)CharFlags.Letter,   // 286 / U+011E
        (byte)CharFlags.Letter,   // 287 / U+011F

        // U+0120 .. U+013F
        (byte)CharFlags.Letter,   // 288 / U+0120
        (byte)CharFlags.Letter,   // 289 / U+0121
        (byte)CharFlags.Letter,   // 290 / U+0122
        (byte)CharFlags.Letter,   // 291 / U+0123
        (byte)CharFlags.Letter,   // 292 / U+0124
        (byte)CharFlags.Letter,   // 293 / U+0125
        (byte)CharFlags.Letter,   // 294 / U+0126
        (byte)CharFlags.Letter,   // 295 / U+0127
        (byte)CharFlags.Letter,   // 296 / U+0128
        (byte)CharFlags.Letter,   // 297 / U+0129
        (byte)CharFlags.Letter,   // 298 / U+012A
        (byte)CharFlags.Letter,   // 299 / U+012B
        (byte)CharFlags.Letter,   // 300 / U+012C
        (byte)CharFlags.Letter,   // 301 / U+012D
        (byte)CharFlags.Letter,   // 302 / U+012E
        (byte)CharFlags.Letter,   // 303 / U+012F
        (byte)CharFlags.Letter,   // 304 / U+0130
        (byte)CharFlags.Letter,   // 305 / U+0131
        (byte)CharFlags.Letter,   // 306 / U+0132
        (byte)CharFlags.Letter,   // 307 / U+0133
        (byte)CharFlags.Letter,   // 308 / U+0134
        (byte)CharFlags.Letter,   // 309 / U+0135
        (byte)CharFlags.Letter,   // 310 / U+0136
        (byte)CharFlags.Letter,   // 311 / U+0137
        (byte)CharFlags.Letter,   // 312 / U+0138
        (byte)CharFlags.Letter,   // 313 / U+0139
        (byte)CharFlags.Letter,   // 314 / U+013A
        (byte)CharFlags.Letter,   // 315 / U+013B
        (byte)CharFlags.Letter,   // 316 / U+013C
        (byte)CharFlags.Letter,   // 317 / U+013D
        (byte)CharFlags.Letter,   // 318 / U+013E
        (byte)CharFlags.Letter,   // 319 / U+013F

        // U+0140 .. U+015F
        (byte)CharFlags.Letter,   // 320 / U+0140
        (byte)CharFlags.Letter,   // 321 / U+0141
        (byte)CharFlags.Letter,   // 322 / U+0142
        (byte)CharFlags.Letter,   // 323 / U+0143
        (byte)CharFlags.Letter,   // 324 / U+0144
        (byte)CharFlags.Letter,   // 325 / U+0145
        (byte)CharFlags.Letter,   // 326 / U+0146
        (byte)CharFlags.Letter,   // 327 / U+0147
        (byte)CharFlags.Letter,   // 328 / U+0148
        (byte)CharFlags.Letter,   // 329 / U+0149
        (byte)CharFlags.Letter,   // 330 / U+014A
        (byte)CharFlags.Letter,   // 331 / U+014B
        (byte)CharFlags.Letter,   // 332 / U+014C
        (byte)CharFlags.Letter,   // 333 / U+014D
        (byte)CharFlags.Letter,   // 334 / U+014E
        (byte)CharFlags.Letter,   // 335 / U+014F
        (byte)CharFlags.Letter,   // 336 / U+0150
        (byte)CharFlags.Letter,   // 337 / U+0151
        (byte)CharFlags.Letter,   // 338 / U+0152
        (byte)CharFlags.Letter,   // 339 / U+0153
        (byte)CharFlags.Letter,   // 340 / U+0154
        (byte)CharFlags.Letter,   // 341 / U+0155
        (byte)CharFlags.Letter,   // 342 / U+0156
        (byte)CharFlags.Letter,   // 343 / U+0157
        (byte)CharFlags.Letter,   // 344 / U+0158
        (byte)CharFlags.Letter,   // 345 / U+0159
        (byte)CharFlags.Letter,   // 346 / U+015A
        (byte)CharFlags.Letter,   // 347 / U+015B
        (byte)CharFlags.Letter,   // 348 / U+015C
        (byte)CharFlags.Letter,   // 349 / U+015D
        (byte)CharFlags.Letter,   // 350 / U+015E
        (byte)CharFlags.Letter,   // 351 / U+015F

        // U+0160 .. U+017F
        (byte)CharFlags.Letter,   // 352 / U+0160
        (byte)CharFlags.Letter,   // 353 / U+0161
        (byte)CharFlags.Letter,   // 354 / U+0162
        (byte)CharFlags.Letter,   // 355 / U+0163
        (byte)CharFlags.Letter,   // 356 / U+0164
        (byte)CharFlags.Letter,   // 357 / U+0165
        (byte)CharFlags.Letter,   // 358 / U+0166
        (byte)CharFlags.Letter,   // 359 / U+0167
        (byte)CharFlags.Letter,   // 360 / U+0168
        (byte)CharFlags.Letter,   // 361 / U+0169
        (byte)CharFlags.Letter,   // 362 / U+016A
        (byte)CharFlags.Letter,   // 363 / U+016B
        (byte)CharFlags.Letter,   // 364 / U+016C
        (byte)CharFlags.Letter,   // 365 / U+016D
        (byte)CharFlags.Letter,   // 366 / U+016E
        (byte)CharFlags.Letter,   // 367 / U+016F
        (byte)CharFlags.Letter,   // 368 / U+0170
        (byte)CharFlags.Letter,   // 369 / U+0171
        (byte)CharFlags.Letter,   // 370 / U+0172
        (byte)CharFlags.Letter,   // 371 / U+0173
        (byte)CharFlags.Letter,   // 372 / U+0174
        (byte)CharFlags.Letter,   // 373 / U+0175
        (byte)CharFlags.Letter,   // 374 / U+0176
        (byte)CharFlags.Letter,   // 375 / U+0177
        (byte)CharFlags.Letter,   // 376 / U+0178
        (byte)CharFlags.Letter,   // 377 / U+0179
        (byte)CharFlags.Letter,   // 378 / U+017A
        (byte)CharFlags.Letter,   // 379 / U+017B
        (byte)CharFlags.Letter,   // 380 / U+017C
        (byte)CharFlags.Letter,   // 381 / U+017D
        (byte)CharFlags.Letter,   // 382 / U+017E
        (byte)CharFlags.Letter,   // 383 / U+017F
    ];

#if STATS
    private void RecordQuickScannerHit()
    {
        if (_collectQuickScannerStats)
        {
            QuickScannerHitCount++;
        }
    }

    private void RecordQuickScannerFallback()
    {
        if (_collectQuickScannerStats)
        {
            QuickScannerFallbackCount++;
        }
    }
#endif
}
