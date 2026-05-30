using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using System.Diagnostics;

namespace Akbura.Language;

internal sealed partial class Lexer
{
	/// <summary>
	/// Small deterministic scanner states used by the quick path.
	///
	/// The regular lexer below is still the source of truth. This scanner is only
	/// allowed to accept short, ordinary DSL tokens that are obviously complete in
	/// the current <see cref="SlidingTextWindow"/> chunk. Anything that might need
	/// Roslyn's C# parser, unicode identifier rules, comments, diagnostics, numeric
	/// suffixes, escape handling, or cross-window reads returns <see cref="Bad"/>
	/// and lets the existing lexer do the real work.
	/// </summary>
	private enum QuickScanState : byte
	{
		Initial,
		Whitespace,
		NewLine,
		Identifier,
		Number,
		Punctuation,
		Done,
		Bad
	}

	private readonly struct QuickTokenData
	{
		public QuickTokenData(
			SyntaxKind kind,
			GreenNode? leading,
			GreenNode? trailing,
			string? text,
			int intValue)
		{
			Kind = kind;
			Leading = leading;
			Trailing = trailing;
			Text = text;
			IntValue = intValue;
		}

		public readonly SyntaxKind Kind;
		public readonly GreenNode? Leading;
		public readonly GreenNode? Trailing;
		public readonly string? Text;
		public readonly int IntValue;
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

		var leadingEnd = 0;
		if (!TryQuickScanTrivia(span, ref leadingEnd, isTrailing: false, _leadingTriviaCache, out var leading))
		{
			return false;
		}

		if (leadingEnd >= span.Length)
		{
			return false;
		}

		if (!TryQuickScanSyntaxToken(
			mode,
			span,
			leadingEnd,
			out var kind,
			out var tokenWidth,
			out var tokenText,
			out var intValue))
		{
			return false;
		}

		var trailingEnd = leadingEnd + tokenWidth;
		if (!TryQuickScanTrivia(span, ref trailingEnd, isTrailing: true, _trailingTriviaCache, out var trailing))
		{
			return false;
		}

		var fullWidth = trailingEnd;
		if (fullWidth <= 0 || fullWidth > MaxCachedTokenSize)
		{
			return false;
		}

		// If we consumed exactly to the end of the current window, but not to EOF,
		// the next chunk could still contain token/trivia continuation. Falling
		// back keeps the quick path from guessing across the sliding-window seam.
		if (fullWidth == span.Length &&
			TextWindow.Position + fullWidth < TextWindow.Text.Length)
		{
			return false;
		}

		var fullTokenSpan = span[..fullWidth];
		var hashCode = HashCode.GetFNVHashCode(fullTokenSpan);
		var data = new QuickTokenData(kind, leading, trailing, tokenText, intValue);

		token = _cache.LookupToken(
			fullTokenSpan,
			hashCode,
			static data => CreateQuickToken(data),
			data);

		TextWindow.AdvanceChar(fullWidth);
		return true;
	}

	private bool TryQuickScanTrivia(
		ReadOnlySpan<char> span,
		ref int index,
		bool isTrailing,
		GreenSyntaxListBuilder builder,
		out GreenNode? trivia)
	{
		builder.Clear();
		trivia = null;

		var state = QuickScanState.Initial;

		while (index < span.Length)
		{
			var ch = span[index];

			state = ch switch
			{
				' ' or '\t' or '\v' or '\f' or '\u001A' => QuickScanState.Whitespace,
				'\r' or '\n' => QuickScanState.NewLine,
				'/' => QuickScanState.Bad,
				> (char)127 => QuickScanState.Bad,
				_ => QuickScanState.Done
			};

			switch (state)
			{
				case QuickScanState.Whitespace:
					builder.Add(QuickScanWhitespaceTrivia(span, ref index));
					continue;

				case QuickScanState.NewLine:
					builder.Add(QuickScanNewLineTrivia(span, ref index));

					// This mirrors LexSyntaxTrivia: trailing trivia is allowed to
					// consume the newline that ends the token, then must stop so
					// the next token starts on the following line.
					if (isTrailing)
					{
						trivia = builder.ToListNode();
						return true;
					}

					continue;

				case QuickScanState.Bad:
					// A slash may be the beginning of // or /* trivia. Comments
					// can contain arbitrary text and diagnostics, so the regular
					// lexer owns that path. A non-ASCII whitespace/newline also
					// belongs to the regular lexer because it normalizes via
					// SyntaxFacts.
					if (ch == '/' &&
						index + 1 < span.Length &&
						span[index + 1] is not ('/' or '*'))
					{
						trivia = builder.ToListNode();
						return true;
					}

					return false;

				case QuickScanState.Done:
					trivia = builder.ToListNode();
					return true;
			}
		}

		trivia = builder.ToListNode();
		return true;
	}

	private GreenSyntaxTrivia QuickScanWhitespaceTrivia(ReadOnlySpan<char> span, ref int index)
	{
		var start = index;
		var state = QuickScanState.Whitespace;

		while (state == QuickScanState.Whitespace && index < span.Length)
		{
			switch (span[index])
			{
				case ' ':
				case '\t':
				case '\v':
				case '\f':
				case '\u001A':
					index++;
					break;

				default:
					state = QuickScanState.Done;
					break;
			}
		}

		var text = span.Slice(start, index - start);
		if (text.Length == 1)
		{
			return text[0] switch
			{
				' ' => GreenSyntaxFactory.Space,
				'\t' => GreenSyntaxFactory.Tab,
				_ => GreenSyntaxFactory.Whitespace(TextWindow.Intern(text))
			};
		}

		return GreenSyntaxFactory.Whitespace(TextWindow.Intern(text));
	}

	private static GreenSyntaxTrivia QuickScanNewLineTrivia(ReadOnlySpan<char> span, ref int index)
	{
		if (span[index] == '\r')
		{
			if (index + 1 < span.Length && span[index + 1] == '\n')
			{
				index += 2;
				return GreenSyntaxFactory.CarriageReturnLineFeed;
			}

			index++;
			return GreenSyntaxFactory.CarriageReturn;
		}

		Debug.Assert(span[index] == '\n');
		index++;
		return GreenSyntaxFactory.LineFeed;
	}

	private bool TryQuickScanSyntaxToken(
		LexerMode mode,
		ReadOnlySpan<char> span,
		int index,
		out SyntaxKind kind,
		out int width,
		out string? text,
		out int intValue)
	{
		kind = SyntaxKind.None;
		width = 0;
		text = null;
		intValue = 0;

		var ch = span[index];
		var state = QuickScanState.Initial;

		state = ch switch
		{
			'_' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') => QuickScanState.Identifier,
			>= '0' and <= '9' => QuickScanState.Number,
			'\'' or '"' or '+' or '-' or '*' or '%' or '^' or '|' or '&' or '?' or ':' or ';' or ',' or '.' or '=' or '!' or '<' or '>' or '{' or '}' or '[' or ']' or '(' or ')' or '/' => QuickScanState.Punctuation,
			'@' when mode == LexerMode.InAkcss => QuickScanState.Punctuation,
			_ => QuickScanState.Bad
		};

		return state switch
		{
			QuickScanState.Identifier => TryQuickScanIdentifier(span, index, out kind, out width, out text),
			QuickScanState.Number => TryQuickScanDecimalInt32(span, index, out kind, out width, out text, out intValue),
			QuickScanState.Punctuation => TryQuickScanPunctuation(mode, span, index, out kind, out width),
			_ => false
		};
	}

	private bool TryQuickScanIdentifier(
		ReadOnlySpan<char> span,
		int index,
		out SyntaxKind kind,
		out int width,
		out string text)
	{
		kind = SyntaxKind.IdentifierToken;
		width = 0;
		text = string.Empty;

		var current = index;
		while (current < span.Length)
		{
			var ch = span[current];
			var isPart =
				ch == '_' ||
				(ch >= 'a' && ch <= 'z') ||
				(ch >= 'A' && ch <= 'Z') ||
				(ch >= '0' && ch <= '9');

			if (!isPart)
			{
				if (IsQuickIdentifierTerminator(ch))
				{
					break;
				}

				return false;
			}

			current++;
		}

		width = current - index;
		if (width <= 0 || width > MaxCachedTokenSize)
		{
			return false;
		}

		text = TextWindow.Intern(span.Slice(index, width));

		if (_cache.TryGetKeywordKind(text, out var keywordKind))
		{
			kind = keywordKind;
		}

		return true;
	}

	private static bool IsQuickIdentifierTerminator(char ch)
	{
		return ch switch
		{
			'\0' or
			' ' or '\r' or '\n' or '\t' or '\v' or '\f' or
			'!' or '%' or '(' or ')' or '*' or '+' or ',' or '-' or '.' or '/' or
			':' or ';' or '<' or '=' or '>' or '?' or '[' or ']' or '^' or '{' or
			'|' or '}' or '~' or '"' or '\'' or '&' or '@' => true,
			_ => false
		};
	}

	private bool TryQuickScanDecimalInt32(
		ReadOnlySpan<char> span,
		int index,
		out SyntaxKind kind,
		out int width,
		out string text,
		out int intValue)
	{
		kind = SyntaxKind.NumericLiteralToken;
		width = 0;
		text = string.Empty;
		intValue = 0;

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
			if (ch < '0' || ch > '9')
			{
				break;
			}

			var digit = ch - '0';
			if (value > (int.MaxValue - digit) / 10)
			{
				return false;
			}

			value = (value * 10) + digit;
			current++;
		}

		if (current < span.Length)
		{
			var next = span[current];
			if (next == '_' ||
				next == '.' ||
				(next >= 'a' && next <= 'z') ||
				(next >= 'A' && next <= 'Z'))
			{
				return false;
			}
		}

		width = current - index;
		if (width <= 0 || width > MaxCachedTokenSize)
		{
			return false;
		}

		text = TextWindow.Intern(span.Slice(index, width));
		intValue = value;
		return true;
	}

	private static bool TryQuickScanPunctuation(
		LexerMode mode,
		ReadOnlySpan<char> span,
		int index,
		out SyntaxKind kind,
		out int width)
	{
		kind = SyntaxKind.None;
		width = 0;

		var ch = span[index];
		var next = index + 1 < span.Length ? span[index + 1] : SlidingTextWindow.InvalidCharacter;

		switch (ch)
		{
			case '\'':
				kind = SyntaxKind.SingleQuoteToken;
				width = 1;
				return true;

			case '"':
				kind = SyntaxKind.DoubleQuoteToken;
				width = 1;
				return true;

			case '/':
				if (next == '>')
				{
					kind = SyntaxKind.SlashGreaterToken;
					width = 2;
					return true;
				}

				if (next is '/' or '*')
				{
					return false;
				}

				kind = SyntaxKind.SlashToken;
				width = 1;
				return true;

			case '.':
				if (next == '.')
				{
					kind = SyntaxKind.DoubleDotToken;
					width = 2;
					return true;
				}

				if (next is >= '0' and <= '9')
				{
					return false;
				}

				kind = SyntaxKind.DotToken;
				width = 1;
				return true;

			case ':':
				if (next == ':')
				{
					kind = SyntaxKind.DoubleColonToken;
					width = 2;
					return true;
				}

				kind = SyntaxKind.ColonToken;
				width = 1;
				return true;

			case '=':
				if (next == '>')
				{
					kind = SyntaxKind.ArrowToken;
					width = 2;
					return true;
				}

				if (next == '=')
				{
					kind = SyntaxKind.EqualsEqualsToken;
					width = 2;
					return true;
				}

				kind = SyntaxKind.EqualsToken;
				width = 1;
				return true;

			case '!':
				if (next == '=')
				{
					kind = SyntaxKind.BangEqualsToken;
					width = 2;
					return true;
				}

				kind = SyntaxKind.BangToken;
				width = 1;
				return true;

			case '<':
				if (next == '/')
				{
					kind = SyntaxKind.LessSlashToken;
					width = 2;
					return true;
				}

				if (next == '=')
				{
					kind = SyntaxKind.LessEqualsToken;
					width = 2;
					return true;
				}

				kind = SyntaxKind.LessThanToken;
				width = 1;
				return true;

			case '>':
				if (next == '=')
				{
					kind = SyntaxKind.GreaterEqualsToken;
					width = 2;
					return true;
				}

				kind = SyntaxKind.GreaterThanToken;
				width = 1;
				return true;

			case '+':
				kind = SyntaxKind.PlusToken;
				width = 1;
				return true;

			case '-':
				kind = SyntaxKind.MinusToken;
				width = 1;
				return true;

			case '*':
				kind = SyntaxKind.AsteriskToken;
				width = 1;
				return true;

			case '%':
				kind = SyntaxKind.PercentToken;
				width = 1;
				return true;

			case '^':
				kind = SyntaxKind.CaretToken;
				width = 1;
				return true;

			case '|':
				kind = SyntaxKind.BarToken;
				width = 1;
				return true;

			case '&':
				kind = SyntaxKind.AmpersandToken;
				width = 1;
				return true;

			case '?':
				kind = SyntaxKind.QuestionToken;
				width = 1;
				return true;

			case ';':
				kind = SyntaxKind.SemicolonToken;
				width = 1;
				return true;

			case ',':
				kind = SyntaxKind.CommaToken;
				width = 1;
				return true;

			case '{':
				kind = SyntaxKind.OpenBraceToken;
				width = 1;
				return true;

			case '}':
				kind = SyntaxKind.CloseBraceToken;
				width = 1;
				return true;

			case '[':
				kind = SyntaxKind.OpenBracketToken;
				width = 1;
				return true;

			case ']':
				kind = SyntaxKind.CloseBracketToken;
				width = 1;
				return true;

			case '(':
				kind = SyntaxKind.OpenParenToken;
				width = 1;
				return true;

			case ')':
				kind = SyntaxKind.CloseParenToken;
				width = 1;
				return true;

			case '@' when mode == LexerMode.InAkcss:
				kind = SyntaxKind.AtToken;
				width = 1;
				return true;

			default:
				return false;
		}
	}

	private static GreenSyntaxToken CreateQuickToken(QuickTokenData data)
	{
		return data.Kind switch
		{
			SyntaxKind.IdentifierToken => GreenSyntaxFactory.Identifier(data.Leading, data.Text!, data.Trailing),
			SyntaxKind.NumericLiteralToken => GreenSyntaxFactory.Literal(data.Leading, data.Text!, data.IntValue, data.Trailing),
			_ => GreenSyntaxFactory.Token(data.Leading, data.Kind, data.Trailing)
		};
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
