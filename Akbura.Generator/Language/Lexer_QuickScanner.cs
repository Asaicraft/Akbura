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
        CompoundPunctStart,
        Slash,
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
        CompoundPunctStart,
        Slash,
        Complex,
        EndOfFile
    }

    // PERF: use byte instead of QuickScanState so the runtime can initialize the
    // multidimensional array directly, same shape as Roslyn's quick scanner.
    private static readonly byte[,] s_stateTransitions = new byte[,]
    {
        // Initial
        {
            (byte)QuickScanState.Initial,             // White
            (byte)QuickScanState.Initial,             // CR
            (byte)QuickScanState.Initial,             // LF
            (byte)QuickScanState.Ident,               // Letter
            (byte)QuickScanState.Number,              // Digit
            (byte)QuickScanState.Punctuation,         // Punct
            (byte)QuickScanState.Dot,                 // Dot
            (byte)QuickScanState.CompoundPunctStart,  // CompoundPunctStart
            (byte)QuickScanState.Slash,               // Slash
            (byte)QuickScanState.Bad,                 // Complex
            (byte)QuickScanState.Bad,                 // EndOfFile
        },

        // FollowingWhite
        {
            (byte)QuickScanState.FollowingWhite,      // White
            (byte)QuickScanState.FollowingCR,         // CR
            (byte)QuickScanState.DoneAfterNext,       // LF
            (byte)QuickScanState.Done,                // Letter
            (byte)QuickScanState.Done,                // Digit
            (byte)QuickScanState.Done,                // Punct
            (byte)QuickScanState.Done,                // Dot
            (byte)QuickScanState.Done,                // CompoundPunctStart
            (byte)QuickScanState.Bad,                 // Slash could start trailing comment
            (byte)QuickScanState.Bad,                 // Complex
            (byte)QuickScanState.Done,                // EndOfFile
        },

        // FollowingCR
        {
            (byte)QuickScanState.Done,                // White
            (byte)QuickScanState.Done,                // CR
            (byte)QuickScanState.DoneAfterNext,       // LF
            (byte)QuickScanState.Done,                // Letter
            (byte)QuickScanState.Done,                // Digit
            (byte)QuickScanState.Done,                // Punct
            (byte)QuickScanState.Done,                // Dot
            (byte)QuickScanState.Done,                // CompoundPunctStart
            (byte)QuickScanState.Done,                // Slash
            (byte)QuickScanState.Done,                // Complex
            (byte)QuickScanState.Done,                // EndOfFile
        },

        // Identifier
        {
            (byte)QuickScanState.FollowingWhite,      // White
            (byte)QuickScanState.FollowingCR,         // CR
            (byte)QuickScanState.DoneAfterNext,       // LF
            (byte)QuickScanState.Ident,               // Letter
            (byte)QuickScanState.Ident,               // Digit
            (byte)QuickScanState.Done,                // Punct
            (byte)QuickScanState.Done,                // Dot
            (byte)QuickScanState.Done,                // CompoundPunctStart
            (byte)QuickScanState.Bad,                 // Slash could start trailing comment
            (byte)QuickScanState.Bad,                 // Complex could be identifier continuation
            (byte)QuickScanState.Done,                // EndOfFile
        },

        // Number
        {
            (byte)QuickScanState.FollowingWhite,      // White
            (byte)QuickScanState.FollowingCR,         // CR
            (byte)QuickScanState.DoneAfterNext,       // LF
            (byte)QuickScanState.Bad,                 // Letter: suffix/base marker/etc.
            (byte)QuickScanState.Number,              // Digit
            (byte)QuickScanState.Done,                // Punct
            (byte)QuickScanState.Bad,                 // Dot: decimal/range ambiguity
            (byte)QuickScanState.Done,                // CompoundPunctStart
            (byte)QuickScanState.Bad,                 // Slash could start trailing comment
            (byte)QuickScanState.Bad,                 // Complex
            (byte)QuickScanState.Done,                // EndOfFile
        },

        // Punctuation
        {
            (byte)QuickScanState.FollowingWhite,      // White
            (byte)QuickScanState.FollowingCR,         // CR
            (byte)QuickScanState.DoneAfterNext,       // LF
            (byte)QuickScanState.Done,                // Letter
            (byte)QuickScanState.Done,                // Digit
            (byte)QuickScanState.Done,                // Punct
            (byte)QuickScanState.Done,                // Dot
            (byte)QuickScanState.Done,                // CompoundPunctStart
            (byte)QuickScanState.Bad,                 // Slash could start trailing comment
            (byte)QuickScanState.Bad,                 // Complex
            (byte)QuickScanState.Done,                // EndOfFile
        },

        // Dot
        {
            (byte)QuickScanState.FollowingWhite,      // White
            (byte)QuickScanState.FollowingCR,         // CR
            (byte)QuickScanState.DoneAfterNext,       // LF
            (byte)QuickScanState.Done,                // Letter
            (byte)QuickScanState.Bad,                 // .0 ambiguity
            (byte)QuickScanState.Done,                // Punct
            (byte)QuickScanState.Bad,                 // DotDot is handled by the DSL patch below
            (byte)QuickScanState.Done,                // CompoundPunctStart
            (byte)QuickScanState.Bad,                 // Slash could start trailing comment
            (byte)QuickScanState.Bad,                 // Complex
            (byte)QuickScanState.Done,                // EndOfFile
        },

        // CompoundPunctStart
        {
            (byte)QuickScanState.FollowingWhite,      // White
            (byte)QuickScanState.FollowingCR,         // CR
            (byte)QuickScanState.DoneAfterNext,       // LF
            (byte)QuickScanState.Done,                // Letter
            (byte)QuickScanState.Done,                // Digit
            (byte)QuickScanState.Bad,                 // Punct
            (byte)QuickScanState.Done,                // Dot
            (byte)QuickScanState.Bad,                 // CompoundPunctStart
            (byte)QuickScanState.Bad,                 // Slash
            (byte)QuickScanState.Bad,                 // Complex
            (byte)QuickScanState.Done,                // EndOfFile
        },

        // Slash
        {
            (byte)QuickScanState.FollowingWhite,      // White
            (byte)QuickScanState.FollowingCR,         // CR
            (byte)QuickScanState.DoneAfterNext,       // LF
            (byte)QuickScanState.Done,                // Letter
            (byte)QuickScanState.Done,                // Digit
            (byte)QuickScanState.Done,                // Punct
            (byte)QuickScanState.Done,                // Dot
            (byte)QuickScanState.Done,                // CompoundPunctStart, except /* below
            (byte)QuickScanState.Bad,                 // Slash: // comment
            (byte)QuickScanState.Bad,                 // Complex
            (byte)QuickScanState.Done,                // EndOfFile
        },

        // DoneAfterNext
        {
            (byte)QuickScanState.Done,                // White
            (byte)QuickScanState.Done,                // CR
            (byte)QuickScanState.Done,                // LF
            (byte)QuickScanState.Done,                // Letter
            (byte)QuickScanState.Done,                // Digit
            (byte)QuickScanState.Done,                // Punct
            (byte)QuickScanState.Done,                // Dot
            (byte)QuickScanState.Done,                // CompoundPunctStart
            (byte)QuickScanState.Done,                // Slash
            (byte)QuickScanState.Done,                // Complex
            (byte)QuickScanState.Done,                // EndOfFile
        },
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

        var span = TextWindow.CurrentWindowSpan;
        if (span.IsEmpty)
        {
            return false;
        }

        var scanSpan = span[..Math.Min(MaxCachedTokenSize, span.Length)];
        var charProperties = CharProperties;
        var charPropertiesLength = charProperties.Length;
        var state = QuickScanState.Initial;
        var hashCode = HashCode.FnvOffsetBias;
        var previousIncludedChar = '\0';

        var currentIndex = 0;
        for (; currentIndex < scanSpan.Length; currentIndex++)
        {
            var c = scanSpan[currentIndex];
            var uc = unchecked((int)c);
            var flags = uc < charPropertiesLength
                ? (CharFlags)charProperties[uc]
                : CharFlags.Complex;

            // DSL-specific token: Roslyn classifies '@' as Complex. In AkCSS mode
            // it is a normal one-character token, so keep it on the quick path.
            if (c == '@' && mode == LexerMode.InAkcss)
            {
                flags = CharFlags.Punct;
            }

            var nextState = (QuickScanState)s_stateTransitions[(int)state, (int)flags];

            // DSL-specific two-character punctuators. Everything else follows the
            // same conservative state table as Roslyn and falls back on ambiguous
            // punctuation chains.
            if (state == QuickScanState.Dot && c == '.')
            {
                nextState = QuickScanState.Punctuation;
            }
            else if (state == QuickScanState.Slash)
            {
                if (c == '>')
                {
                    nextState = QuickScanState.Punctuation;
                }
                else if (c == '*')
                {
                    nextState = QuickScanState.Bad;
                }
            }
            else if (state == QuickScanState.CompoundPunctStart)
            {
                switch (previousIncludedChar)
                {
                    case ':' when c == ':':
                    case '=' when c is '>' or '=':
                    case '!' when c == '=':
                    case '<' when c is '/' or '=':
                    case '>' when c == '=':
                        nextState = QuickScanState.Punctuation;
                        break;

                    // In this DSL these compound-start characters are also valid
                    // one-character tokens. Roslyn falls back on punctuation chains,
                    // but markup has many safe cases like class="..." where falling
                    // back on '=' costs hit-rate. Keep '~' conservative because the
                    // previous quick scanner did not accept it as a token.
                    default:
                        if (previousIncludedChar != '~' && nextState == QuickScanState.Bad)
                        {
                            nextState = QuickScanState.Done;
                        }

                        break;
                }
            }

            if (nextState >= QuickScanState.Done)
            {
                state = nextState;
                goto exitLoop;
            }

            state = nextState;
            hashCode = HashCode.CombineFNVHash(hashCode, c);
            previousIncludedChar = c;
        }

        // We reached the end of scanSpan without seeing a terminator. Roslyn normally
        // observes an EOF sentinel in the window and transitions through the table.
        // This text window can end exactly at real EOF, so synthesize that transition
        // without hashing or consuming an EOF character. If this is only a window /
        // MaxCachedTokenSize boundary, keep the conservative fallback.
        if (TextWindow.Position + currentIndex >= TextWindow.Text.Length)
        {
            state = (QuickScanState)s_stateTransitions[(int)state, (int)CharFlags.EndOfFile];
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

        var fullWidth = currentIndex;
        if (fullWidth <= 0 || fullWidth > MaxCachedTokenSize)
        {
            return false;
        }

        // If we reached the current window boundary, the next window could still
        // continue a token or trivia. Fall back instead of guessing across seams.
        if (fullWidth == scanSpan.Length &&
            TextWindow.Position + fullWidth < TextWindow.Text.Length)
        {
            return false;
        }

        var fullTokenSpan = span[..fullWidth];
        TextWindow.AdvanceChar(fullWidth);

        token = _cache.LookupToken(
            fullTokenSpan,
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

    private static ReadOnlySpan<byte> CharProperties => new[]
            {
                // 0 .. 31
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
                (byte)CharFlags.Complex,
                (byte)CharFlags.White,   // TAB
                (byte)CharFlags.LF,      // LF
                (byte)CharFlags.White,   // VT
                (byte)CharFlags.White,   // FF
                (byte)CharFlags.CR,      // CR
                (byte)CharFlags.Complex,
                (byte)CharFlags.Complex,
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,

                // 32 .. 63
                (byte)CharFlags.White,    // SPC
                (byte)CharFlags.CompoundPunctStart,    // !
                (byte)CharFlags.Punct,    // "
                (byte)CharFlags.Complex,  // #
                (byte)CharFlags.Complex,  // $
                (byte)CharFlags.CompoundPunctStart, // %
                (byte)CharFlags.CompoundPunctStart, // &
                (byte)CharFlags.Punct,    // '
                (byte)CharFlags.Punct,    // (
                (byte)CharFlags.Punct,    // )
                (byte)CharFlags.CompoundPunctStart, // *
                (byte)CharFlags.CompoundPunctStart, // +
                (byte)CharFlags.Punct,    // ,
                (byte)CharFlags.CompoundPunctStart, // -
                (byte)CharFlags.Dot,      // .
                (byte)CharFlags.Slash,    // /
                (byte)CharFlags.Digit,    // 0
                (byte)CharFlags.Digit,    // 1
                (byte)CharFlags.Digit,    // 2
                (byte)CharFlags.Digit,    // 3
                (byte)CharFlags.Digit,    // 4
                (byte)CharFlags.Digit,    // 5
                (byte)CharFlags.Digit,    // 6
                (byte)CharFlags.Digit,    // 7
                (byte)CharFlags.Digit,    // 8
                (byte)CharFlags.Digit,    // 9
                (byte)CharFlags.CompoundPunctStart,  // :
                (byte)CharFlags.Punct,    // ;
                (byte)CharFlags.CompoundPunctStart,  // <
                (byte)CharFlags.CompoundPunctStart,  // =
                (byte)CharFlags.CompoundPunctStart,  // >
                (byte)CharFlags.CompoundPunctStart,  // ?

                // 64 .. 95
                (byte)CharFlags.Complex,  // @
                (byte)CharFlags.Letter,   // A
                (byte)CharFlags.Letter,   // B
                (byte)CharFlags.Letter,   // C
                (byte)CharFlags.Letter,   // D
                (byte)CharFlags.Letter,   // E
                (byte)CharFlags.Letter,   // F
                (byte)CharFlags.Letter,   // G
                (byte)CharFlags.Letter,   // H
                (byte)CharFlags.Letter,   // I
                (byte)CharFlags.Letter,   // J
                (byte)CharFlags.Letter,   // K
                (byte)CharFlags.Letter,   // L
                (byte)CharFlags.Letter,   // M
                (byte)CharFlags.Letter,   // N
                (byte)CharFlags.Letter,   // O
                (byte)CharFlags.Letter,   // P
                (byte)CharFlags.Letter,   // Q
                (byte)CharFlags.Letter,   // R
                (byte)CharFlags.Letter,   // S
                (byte)CharFlags.Letter,   // T
                (byte)CharFlags.Letter,   // U
                (byte)CharFlags.Letter,   // V
                (byte)CharFlags.Letter,   // W
                (byte)CharFlags.Letter,   // X
                (byte)CharFlags.Letter,   // Y
                (byte)CharFlags.Letter,   // Z
                (byte)CharFlags.Punct,    // [
                (byte)CharFlags.Complex,  // \
                (byte)CharFlags.Punct,    // ]
                (byte)CharFlags.CompoundPunctStart,    // ^
                (byte)CharFlags.Letter,   // _

                // 96 .. 127
                (byte)CharFlags.Complex,  // `
                (byte)CharFlags.Letter,   // a
                (byte)CharFlags.Letter,   // b
                (byte)CharFlags.Letter,   // c
                (byte)CharFlags.Letter,   // d
                (byte)CharFlags.Letter,   // e
                (byte)CharFlags.Letter,   // f
                (byte)CharFlags.Letter,   // g
                (byte)CharFlags.Letter,   // h
                (byte)CharFlags.Letter,   // i
                (byte)CharFlags.Letter,   // j
                (byte)CharFlags.Letter,   // k
                (byte)CharFlags.Letter,   // l
                (byte)CharFlags.Letter,   // m
                (byte)CharFlags.Letter,   // n
                (byte)CharFlags.Letter,   // o
                (byte)CharFlags.Letter,   // p
                (byte)CharFlags.Letter,   // q
                (byte)CharFlags.Letter,   // r
                (byte)CharFlags.Letter,   // s
                (byte)CharFlags.Letter,   // t
                (byte)CharFlags.Letter,   // u
                (byte)CharFlags.Letter,   // v
                (byte)CharFlags.Letter,   // w
                (byte)CharFlags.Letter,   // x
                (byte)CharFlags.Letter,   // y
                (byte)CharFlags.Letter,   // z
                (byte)CharFlags.Punct,    // {
                (byte)CharFlags.CompoundPunctStart,  // |
                (byte)CharFlags.Punct,    // }
                (byte)CharFlags.CompoundPunctStart,    // ~
                (byte)CharFlags.Complex,

                // 128 .. 159
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,

                // 160 .. 191
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Letter, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Letter, (byte)CharFlags.Complex, (byte)CharFlags.Complex,
                (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Letter, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex, (byte)CharFlags.Complex,

                // 192 .. 
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Complex,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,

                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Complex,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,

                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,

                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,

                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,

                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
                (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter, (byte)CharFlags.Letter,
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
