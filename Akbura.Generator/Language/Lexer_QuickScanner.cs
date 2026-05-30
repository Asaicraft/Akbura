using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Akbura.Language;

internal sealed partial class Lexer
{
	/// <summary>
	/// Compact ASCII classification for the quick path.
	///
	/// The regular lexer below is still the source of truth. This scanner is only
	/// allowed to accept short, ordinary DSL tokens that are obviously complete in
	/// the current <see cref="SlidingTextWindow"/> chunk. Anything that might need
	/// Roslyn's C# parser, unicode identifier rules, comments, diagnostics, numeric
	/// suffixes, escape handling, or cross-window reads falls back to the regular
	/// lexer.
	/// </summary>
	[Flags]
	private enum QuickCharKind : byte
	{
		Complex = 0,
		White = 1 << 0,
		NewLine = 1 << 1,
		IdentifierStart = 1 << 2,
		IdentifierPart = 1 << 3,
		Digit = 1 << 4,
		Punctuation = 1 << 5,
		Slash = 1 << 6
	}

	private static readonly byte[] s_quickCharKinds = CreateQuickCharKinds();

	private static readonly ushort[] s_singleCharTokenKinds = CreateSingleCharTokenKinds();

	private static byte[] CreateQuickCharKinds()
	{
		var kinds = new byte[128];

		SetQuickCharKind(kinds, '\0', QuickCharKind.Punctuation);

		SetQuickCharKind(kinds, ' ', QuickCharKind.White);
		SetQuickCharKind(kinds, '\t', QuickCharKind.White);
		SetQuickCharKind(kinds, '\v', QuickCharKind.White);
		SetQuickCharKind(kinds, '\f', QuickCharKind.White);
		SetQuickCharKind(kinds, '\u001A', QuickCharKind.White);
		SetQuickCharKind(kinds, '\r', QuickCharKind.NewLine);
		SetQuickCharKind(kinds, '\n', QuickCharKind.NewLine);

		for (var ch = 'a'; ch <= 'z'; ch++)
		{
			SetQuickCharKind(kinds, ch, QuickCharKind.IdentifierStart | QuickCharKind.IdentifierPart);
		}

		for (var ch = 'A'; ch <= 'Z'; ch++)
		{
			SetQuickCharKind(kinds, ch, QuickCharKind.IdentifierStart | QuickCharKind.IdentifierPart);
		}

		SetQuickCharKind(kinds, '_', QuickCharKind.IdentifierStart | QuickCharKind.IdentifierPart);

		for (var ch = '0'; ch <= '9'; ch++)
		{
			SetQuickCharKind(kinds, ch, QuickCharKind.Digit | QuickCharKind.IdentifierPart);
		}

		foreach (var ch in "'\"+-*%^|&?:;,.=!<>{}[]()@~")
		{
			SetQuickCharKind(kinds, ch, QuickCharKind.Punctuation);
		}

		SetQuickCharKind(kinds, '/', QuickCharKind.Slash | QuickCharKind.Punctuation);

		return kinds;
	}

	private static ushort[] CreateSingleCharTokenKinds()
	{
		var kinds = new ushort[128];

		SetSingleCharTokenKind(kinds, '\'', SyntaxKind.SingleQuoteToken);
		SetSingleCharTokenKind(kinds, '"', SyntaxKind.DoubleQuoteToken);
		SetSingleCharTokenKind(kinds, '/', SyntaxKind.SlashToken);
		SetSingleCharTokenKind(kinds, '.', SyntaxKind.DotToken);
		SetSingleCharTokenKind(kinds, ':', SyntaxKind.ColonToken);
		SetSingleCharTokenKind(kinds, '=', SyntaxKind.EqualsToken);
		SetSingleCharTokenKind(kinds, '!', SyntaxKind.BangToken);
		SetSingleCharTokenKind(kinds, '<', SyntaxKind.LessThanToken);
		SetSingleCharTokenKind(kinds, '>', SyntaxKind.GreaterThanToken);
		SetSingleCharTokenKind(kinds, '+', SyntaxKind.PlusToken);
		SetSingleCharTokenKind(kinds, '-', SyntaxKind.MinusToken);
		SetSingleCharTokenKind(kinds, '*', SyntaxKind.AsteriskToken);
		SetSingleCharTokenKind(kinds, '%', SyntaxKind.PercentToken);
		SetSingleCharTokenKind(kinds, '^', SyntaxKind.CaretToken);
		SetSingleCharTokenKind(kinds, '|', SyntaxKind.BarToken);
		SetSingleCharTokenKind(kinds, '&', SyntaxKind.AmpersandToken);
		SetSingleCharTokenKind(kinds, '?', SyntaxKind.QuestionToken);
		SetSingleCharTokenKind(kinds, ';', SyntaxKind.SemicolonToken);
		SetSingleCharTokenKind(kinds, ',', SyntaxKind.CommaToken);
		SetSingleCharTokenKind(kinds, '{', SyntaxKind.OpenBraceToken);
		SetSingleCharTokenKind(kinds, '}', SyntaxKind.CloseBraceToken);
		SetSingleCharTokenKind(kinds, '[', SyntaxKind.OpenBracketToken);
		SetSingleCharTokenKind(kinds, ']', SyntaxKind.CloseBracketToken);
		SetSingleCharTokenKind(kinds, '(', SyntaxKind.OpenParenToken);
		SetSingleCharTokenKind(kinds, ')', SyntaxKind.CloseParenToken);

		return kinds;
	}

	private static void SetQuickCharKind(byte[] kinds, char ch, QuickCharKind kind)
	{
		kinds[ch] = (byte)kind;
	}

	private static void SetSingleCharTokenKind(ushort[] kinds, char ch, SyntaxKind kind)
	{
		kinds[ch] = (ushort)kind;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static QuickCharKind GetQuickCharKind(char ch)
	{
		return ch < s_quickCharKinds.Length
			? (QuickCharKind)s_quickCharKinds[ch]
			: QuickCharKind.Complex;
	}

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

		var span = TextWindow.CurrentWindowSpan;
		if (span.IsEmpty)
		{
			return false;
		}

		// The quick path deliberately scans only width + hash. Real token,
		// trivia, interned text, values, and diagnostics are created by the
		// regular lexer only on a cache miss.
		Start();

		var scanSpan = span.Length > MaxCachedTokenSize
			? span[..MaxCachedTokenSize]
			: span;

		var hashCode = HashCode.FnvOffsetBias;
		if (!TryQuickScanTokenWidthOnly(mode, scanSpan, ref hashCode, out var fullWidth))
		{
			return false;
		}

		if (fullWidth <= 0 || fullWidth > MaxCachedTokenSize)
		{
			return false;
		}

		// If we consumed exactly to the end of the current window, but not to EOF,
		// the next chunk could still contain token/trivia continuation. Falling
		// back keeps the quick path from guessing across the sliding-window seam.
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

	private static bool TryQuickScanTokenWidthOnly(
		LexerMode mode,
		ReadOnlySpan<char> span,
		ref int hashCode,
		out int fullWidth)
	{
		fullWidth = 0;

		var index = 0;
		if (!TryQuickScanTriviaWidthOnly(span, ref index, isTrailing: false, ref hashCode))
		{
			return false;
		}

		if (index >= span.Length)
		{
			return false;
		}

		if (!TryQuickScanSyntaxTokenWidthOnly(mode, span, index, ref hashCode, out var tokenWidth))
		{
			return false;
		}

		index += tokenWidth;
		if (!TryQuickScanTriviaWidthOnly(span, ref index, isTrailing: true, ref hashCode))
		{
			return false;
		}

		fullWidth = index;
		return true;
	}

	private static bool TryQuickScanTriviaWidthOnly(
		ReadOnlySpan<char> span,
		ref int index,
		bool isTrailing,
		ref int hashCode)
	{
		while (index < span.Length)
		{
			var ch = span[index];
			if (ch >= s_quickCharKinds.Length)
			{
				return false;
			}

			var charKind = GetQuickCharKind(ch);
			if ((charKind & QuickCharKind.White) != 0)
			{
				QuickScanWhitespaceTriviaWidthOnly(span, ref index, ref hashCode);
				continue;
			}

			if ((charKind & QuickCharKind.NewLine) != 0)
			{
				QuickScanNewLineTriviaWidthOnly(span, ref index, ref hashCode);

				// This mirrors LexSyntaxTrivia: trailing trivia is allowed to
				// consume the newline that ends the token, then must stop so
				// the next token starts on the following line.
				if (isTrailing)
				{
					return true;
				}

				continue;
			}

			// A slash may be the beginning of // or /* trivia. Comments can contain
			// arbitrary text and diagnostics, so the regular lexer owns that path.
			if ((charKind & QuickCharKind.Slash) != 0 &&
				index + 1 < span.Length &&
				span[index + 1] is '/' or '*')
			{
				return false;
			}

			return true;
		}

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void QuickScanWhitespaceTriviaWidthOnly(ReadOnlySpan<char> span, ref int index, ref int hashCode)
	{
		while (index < span.Length &&
			(GetQuickCharKind(span[index]) & QuickCharKind.White) != 0)
		{
			hashCode = HashCode.CombineFNVHash(hashCode, span[index]);
			index++;
		}
	}

	private static void QuickScanNewLineTriviaWidthOnly(ReadOnlySpan<char> span, ref int index, ref int hashCode)
	{
		if (span[index] == '\r')
		{
			hashCode = HashCode.CombineFNVHash(hashCode, span[index]);

			if (index + 1 < span.Length && span[index + 1] == '\n')
			{
				hashCode = HashCode.CombineFNVHash(hashCode, span[index + 1]);
				index += 2;
				return;
			}

			index++;
			return;
		}

		Debug.Assert(span[index] == '\n');
		hashCode = HashCode.CombineFNVHash(hashCode, span[index]);
		index++;
	}

	private static bool TryQuickScanSyntaxTokenWidthOnly(
		LexerMode mode,
		ReadOnlySpan<char> span,
		int index,
		ref int hashCode,
		out int width)
	{
		width = 0;

		var charKind = GetQuickCharKind(span[index]);
		if ((charKind & QuickCharKind.IdentifierStart) != 0)
		{
			return TryQuickScanIdentifierWidthOnly(span, index, ref hashCode, out width);
		}

		if ((charKind & QuickCharKind.Digit) != 0)
		{
			return TryQuickScanDecimalInt32WidthOnly(span, index, ref hashCode, out width);
		}

		if ((charKind & QuickCharKind.Punctuation) != 0)
		{
			return TryQuickScanPunctuationWidthOnly(mode, span, index, ref hashCode, out width);
		}

		return false;
	}

	private static bool TryQuickScanIdentifierWidthOnly(
		ReadOnlySpan<char> span,
		int index,
		ref int hashCode,
		out int width)
	{
		width = 0;

		var current = index;
		while (current < span.Length)
		{
			var ch = span[current];
			var charKind = GetQuickCharKind(ch);

			if ((charKind & QuickCharKind.IdentifierPart) == 0)
			{
				if (IsQuickIdentifierTerminator(ch, charKind))
				{
					break;
				}

				return false;
			}

			hashCode = HashCode.CombineFNVHash(hashCode, ch);
			current++;
		}

		width = current - index;
		if (width <= 0 || width > MaxCachedTokenSize)
		{
			return false;
		}

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsQuickIdentifierTerminator(char ch, QuickCharKind charKind)
	{
		return ch < s_quickCharKinds.Length &&
			(charKind & (QuickCharKind.White | QuickCharKind.NewLine | QuickCharKind.Punctuation | QuickCharKind.Slash)) != 0;
	}

	private static bool TryQuickScanDecimalInt32WidthOnly(
		ReadOnlySpan<char> span,
		int index,
		ref int hashCode,
		out int width)
	{
		width = 0;

		if (span[index] == '0' &&
			index + 1 < span.Length &&
			span[index + 1] is 'x' or 'X' or 'b' or 'B')
		{
			return false;
		}

		var current = index;
		var value = 0;
		while (current < span.Length)
		{
			var ch = span[current];
			if ((GetQuickCharKind(ch) & QuickCharKind.Digit) == 0)
			{
				break;
			}

			var digit = ch - '0';
			if (value > (int.MaxValue - digit) / 10)
			{
				return false;
			}

			value = (value * 10) + digit;
			hashCode = HashCode.CombineFNVHash(hashCode, ch);
			current++;
		}

		if (current < span.Length)
		{
			var next = span[current];
			var nextKind = GetQuickCharKind(next);
			if (next == '.' ||
				(nextKind & QuickCharKind.IdentifierPart) != 0)
			{
				return false;
			}
		}

		width = current - index;
		if (width <= 0 || width > MaxCachedTokenSize)
		{
			return false;
		}

		return true;
	}

	private static bool TryQuickScanPunctuationWidthOnly(
		LexerMode mode,
		ReadOnlySpan<char> span,
		int index,
		ref int hashCode,
		out int width)
	{
		width = 0;

		var ch = span[index];
		var next = index + 1 < span.Length ? span[index + 1] : SlidingTextWindow.InvalidCharacter;

		switch (ch)
		{
			case '/':
				if (next == '>')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				if (next is '/' or '*')
				{
					return false;
				}

				break;

			case '.':
				if (next == '.')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				if (next is >= '0' and <= '9')
				{
					return false;
				}

				break;

			case ':':
				if (next == ':')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				break;

			case '=':
				if (next == '>')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				if (next == '=')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				break;

			case '!':
				if (next == '=')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				break;

			case '<':
				if (next == '/')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				if (next == '=')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				break;

			case '>':
				if (next == '=')
				{
					return AcceptQuickPunctuationWidthOnly(span, index, 2, ref hashCode, out width);
				}

				break;

			case '@' when mode == LexerMode.InAkcss:
				return AcceptQuickPunctuationWidthOnly(span, index, 1, ref hashCode, out width);

			case '@':
				return false;
		}

		if (ch >= s_singleCharTokenKinds.Length)
		{
			return false;
		}

		var tokenKind = (SyntaxKind)s_singleCharTokenKinds[ch];
		if (tokenKind == SyntaxKind.None)
		{
			return false;
		}

		return AcceptQuickPunctuationWidthOnly(span, index, 1, ref hashCode, out width);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool AcceptQuickPunctuationWidthOnly(
		ReadOnlySpan<char> span,
		int index,
		int acceptedWidth,
		ref int hashCode,
		out int width)
	{
		for (var i = 0; i < acceptedWidth; i++)
		{
			hashCode = HashCode.CombineFNVHash(hashCode, span[index + i]);
		}

		width = acceptedWidth;
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
