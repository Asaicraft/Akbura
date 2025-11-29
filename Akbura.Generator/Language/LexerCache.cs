using Akbura.Collections;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;

namespace Akbura.Language;
internal sealed class LexerCache
{
    private static readonly ObjectPool<LexerCache> s_lexerCachePool = new(() => new LexerCache());

    private static readonly ObjectPool<CachingIdentityFactory<string, SyntaxKind>> s_keywordKindPool =
        CachingIdentityFactory<string, SyntaxKind>.CreatePool(
                        128,
                        (key) =>
                        {
                            var kind = SyntaxFacts.GetKeywordKind(key);
                            if (kind == SyntaxKind.None)
                            {
                                kind = SyntaxFacts.GetContextualKeywordKind(key);
                            }

                            return kind;
                        });

    private TextKeyedCache<GreenSyntaxTrivia>? _triviaMap;
    private TextKeyedCache<GreenSyntaxToken>? _tokenMap;
    private CachingIdentityFactory<string, SyntaxKind>? _keywordKindMap;
    internal const int MaxKeywordLength = 10;

    private PooledStringBuilder? _stringBuilder;
    private readonly char[] _identBuffer;
    private SyntaxListBuilder? _leadingTriviaCache;
    private SyntaxListBuilder? _trailingTriviaCache;

    private const int LeadingTriviaCacheInitialCapacity = 128;
    private const int TrailingTriviaCacheInitialCapacity = 16;

    private LexerCache()
    {
        _identBuffer = new char[32];
    }

    public static LexerCache GetInstance()
    {
        return s_lexerCachePool.Allocate();
    }

    public void Free()
    {
        if (_keywordKindMap != null)
        {
            _keywordKindMap.Free();
            _keywordKindMap = null;
        }

        if (_triviaMap != null)
        {
            _triviaMap.Free();
            _triviaMap = null;
        }

        if (_tokenMap != null)
        {
            _tokenMap.Free();
            _tokenMap = null;
        }

        if (_stringBuilder != null)
        {
            _stringBuilder.Free();
            _stringBuilder = null;
        }

        if (_leadingTriviaCache != null)
        {
            if (_leadingTriviaCache.Capacity > LeadingTriviaCacheInitialCapacity * 2)
            {
                // Don't reuse _leadingTriviaCache if it has grown too large for pooling.
                _leadingTriviaCache = null;
            }
            else
            {
                _leadingTriviaCache.Clear();
            }
        }

        if (_trailingTriviaCache != null)
        {
            if (_trailingTriviaCache.Capacity > TrailingTriviaCacheInitialCapacity * 2)
            {
                // Don't reuse _trailingTriviaCache if it has grown too large for pooling.
                _trailingTriviaCache = null;
            }
            else
            {
                _trailingTriviaCache.Clear();
            }
        }

        s_lexerCachePool.Free(this);
    }

    internal char[] IdentifierBuffer => _identBuffer;

    private TextKeyedCache<GreenSyntaxTrivia> TriviaMap
    {
        get
        {
            _triviaMap ??= TextKeyedCache<GreenSyntaxTrivia>.GetInstance();

            return _triviaMap;
        }
    }

    private TextKeyedCache<GreenSyntaxToken> TokenMap
    {
        get
        {
            _tokenMap ??= TextKeyedCache<GreenSyntaxToken>.GetInstance();

            return _tokenMap;
        }
    }

    private CachingIdentityFactory<string, SyntaxKind> KeywordKindMap
    {
        get
        {
            _keywordKindMap ??= s_keywordKindPool.Allocate();

            return _keywordKindMap;
        }
    }

    internal PooledStringBuilder StringBuilder
    {
        get
        {
            _stringBuilder ??= PooledStringBuilder.GetInstance();

            return _stringBuilder;
        }
    }

    internal SyntaxListBuilder LeadingTriviaCache
    {
        get
        {
            _leadingTriviaCache ??= new SyntaxListBuilder(LeadingTriviaCacheInitialCapacity);

            return _leadingTriviaCache;
        }
    }

    internal SyntaxListBuilder TrailingTriviaCache
    {
        get
        {
            _trailingTriviaCache ??= new SyntaxListBuilder(TrailingTriviaCacheInitialCapacity);

            return _trailingTriviaCache;
        }
    }

    internal bool TryGetKeywordKind(string key, out SyntaxKind kind)
    {
        if (key.Length > MaxKeywordLength)
        {
            kind = SyntaxKind.None;
            return false;
        }

        kind = KeywordKindMap.GetOrMakeValue(key);
        return kind != SyntaxKind.None;
    }

    internal SyntaxTrivia LookupWhitespaceTrivia(
        in SlidingTextWindow textWindow,
        int lexemeStartPosition,
        int hashCode)
    {
        var span = TextSpan.FromBounds(lexemeStartPosition, textWindow.Position);
        Debug.Assert(span.Length > 0);

        if (textWindow.TryGetTextIfWithinWindow(span, out var lexemeTextSpan))
        {
            var value = TriviaMap.FindItem(lexemeTextSpan, hashCode);
            if (value == null)
            {
                value = GreenSyntaxFactory.Whitespace(textWindow.GetText(lexemeStartPosition, intern: true));
                TriviaMap.AddItem(lexemeTextSpan, hashCode, value);
            }

            return value;
        }

        // Otherwise, if it's outside of the window, just grab from the underlying text.
        return SyntaxFactory.Whitespace(textWindow.GetText(lexemeStartPosition, intern: true));
    }

    // TODO: remove this when done tweaking this cache.
#if COLLECT_STATS
        private static int hits = 0;
        private static int misses = 0;

        private static void Hit()
        {
            var h = System.Threading.Interlocked.Increment(ref hits);

            if (h % 10000 == 0)
            {
                Console.WriteLine(h * 100 / (h + misses));
            }
        }

        private static void Miss()
        {
            System.Threading.Interlocked.Increment(ref misses);
        }
#endif

    internal GreenSyntaxToken LookupToken<TArg>(
        ReadOnlySpan<char> textBuffer,
        int hashCode,
        Func<TArg, GreenSyntaxToken> createTokenFunction,
        TArg data)
    {
        var value = TokenMap.FindItem(textBuffer, hashCode);

        if (value == null)
        {
#if COLLECT_STATS
                    Miss();
#endif
            value = createTokenFunction(data);
            TokenMap.AddItem(textBuffer, hashCode, value);
        }
        else
        {
#if COLLECT_STATS
                    Hit();
#endif
        }

        return value;
    }
}