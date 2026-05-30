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

    private static ReadOnlySpan<byte> StateTransitions => new byte[]
    {
        // Initial
        (byte)QuickScanState.Initial, (byte)QuickScanState.Initial, (byte)QuickScanState.Initial, (byte)QuickScanState.Ident, (byte)QuickScanState.Number, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Dot, (byte)QuickScanState.Slash, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Bang, (byte)QuickScanState.Colon, (byte)QuickScanState.Less, (byte)QuickScanState.Equals, (byte)QuickScanState.Greater, (byte)QuickScanState.Bad, (byte)QuickScanState.Bad,
        // FollowingWhite
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // FollowingCR
        (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done,
        // Ident
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Ident, (byte)QuickScanState.Ident, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Number
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Bad, (byte)QuickScanState.Number, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Punctuation
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Dot
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Slash
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Bang
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Colon
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Less
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Equals
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // Greater
        (byte)QuickScanState.FollowingWhite, (byte)QuickScanState.FollowingCR, (byte)QuickScanState.DoneAfterNext, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Punctuation, (byte)QuickScanState.Done, (byte)QuickScanState.Bad, (byte)QuickScanState.Done,
        // DoneAfterNext
        (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done, (byte)QuickScanState.Done,
    };

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

        // Cap how much of the char span we're willing to look at.
        textWindowCharSpan = textWindowCharSpan[..Math.Min(MaxCachedTokenSize, textWindowCharSpan.Length)];

        var charProperties = CharProperties;
        var charPropertiesLength = charProperties.Length;
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

    private static ReadOnlySpan<byte> CharProperties => new byte[]
    {
        // 0 .. 7
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 8 .. 15
        (byte)CharFlags.Complex, (byte)CharFlags.White, (byte)CharFlags.LF, (byte)CharFlags.White, (byte)CharFlags.White, (byte)CharFlags.CR, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 16 .. 23
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 24 .. 31
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.White, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 32 .. 39
        (byte)CharFlags.White, (byte)CharFlags.Bang, (byte)CharFlags.Punct, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Punct, (byte)CharFlags.Punct, (byte)CharFlags.Punct,
        // 40 .. 47
        (byte)CharFlags.Punct, (byte)CharFlags.Punct, (byte)CharFlags.Asterisk, (byte)CharFlags.Punct, (byte)CharFlags.Punct, (byte)CharFlags.Punct, (byte)CharFlags.Dot, (byte)CharFlags.Slash,
        // 48 .. 55
        (byte)CharFlags.Digit, (byte)CharFlags.Digit, (byte)CharFlags.Digit, (byte)CharFlags.Digit, (byte)CharFlags.Digit, (byte)CharFlags.Digit, (byte)CharFlags.Digit, (byte)CharFlags.Digit,
        // 56 .. 63
        (byte)CharFlags.Digit, (byte)CharFlags.Digit, (byte)CharFlags.Colon, (byte)CharFlags.Punct, (byte)CharFlags.Less, (byte)CharFlags.Equals, (byte)CharFlags.Greater, (byte)CharFlags.Punct,
        // 64 .. 71
        (byte)CharFlags.Punct, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 72 .. 79
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 80 .. 87
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 88 .. 95
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Punct, (byte)CharFlags.Complex, (byte)CharFlags.Punct, (byte)CharFlags.Punct, (byte)CharFlags.Letter,
        // 96 .. 103
        (byte)CharFlags.Complex, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 104 .. 111
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 112 .. 119
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 120 .. 127
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Punct, (byte)CharFlags.Punct, (byte)CharFlags.Punct, (byte)CharFlags.Punct, (byte)CharFlags.Complex,
        // 128 .. 135
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 136 .. 143
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 144 .. 151
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 152 .. 159
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 160 .. 167
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 168 .. 175
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Letter, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 176 .. 183
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Letter, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 184 .. 191
        (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Letter, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
        // 192 .. 199
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 200 .. 207
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 208 .. 215
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Complex,
        // 216 .. 223
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 224 .. 231
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 232 .. 239
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 240 .. 247
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Complex,
        // 248 .. 255
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 256 .. 263
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 264 .. 271
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 272 .. 279
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 280 .. 287
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 288 .. 295
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 296 .. 303
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 304 .. 311
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 312 .. 319
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 320 .. 327
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 328 .. 335
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 336 .. 343
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 344 .. 351
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 352 .. 359
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 360 .. 367
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 368 .. 375
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
        // 376 .. 383
        (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter
    };

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
