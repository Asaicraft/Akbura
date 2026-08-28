#nullable enable

namespace Akbura.Language.Syntax
{
	static partial class SyntaxFacts
	{
		public static partial bool IsLiteral(SyntaxKind kind)
		{
			switch (kind)
			{
				case SyntaxKind.StringLiteralToken:
				case SyntaxKind.CharLiteralToken:
				case SyntaxKind.NumericLiteralToken:
				case SyntaxKind.AkTextLiteral:
					return true;
				default:
					return false;
			}
		}

		public static partial bool IsAnyToken(SyntaxKind kind)
		{
			if (kind >= SyntaxKind.FirstTokenWithWellKnownText &&
				kind <= SyntaxKind.LastTokenWithWellKnownText)
			{
				return true;
			}

			switch (kind)
			{
				case SyntaxKind.CSharpRawToken:
				case SyntaxKind.EndOfFileToken:
				case SyntaxKind.IdentifierToken:
				case SyntaxKind.StringLiteralToken:
				case SyntaxKind.CharLiteralToken:
				case SyntaxKind.NumericLiteralToken:
				case SyntaxKind.BadToken:
					return true;

				default:
					return false;
			}
		}

		public static partial bool IsTrivia(SyntaxKind kind)
		{
			switch (kind)
			{
				case SyntaxKind.EndOfLineTrivia:
				case SyntaxKind.WhitespaceTrivia:
				case SyntaxKind.AkTextLiteral:
					return true;
				default:
					return false;
			}
		}

		public static partial string GetText(SyntaxKind kind)
		{
			return kind switch
			{
				SyntaxKind.InjectKeyword => "inject",
				SyntaxKind.ParamKeyword => "param",
				SyntaxKind.StateKeyword => "state",
				SyntaxKind.SuppressKeyword => "suppress",
				SyntaxKind.FinallyKeyword => "finally",
				SyntaxKind.AsyncKeyword => "async",
				SyntaxKind.VoidKeyword => "void",
				SyntaxKind.CommandKeyword => "command",

				SyntaxKind.NewKeyword => "new",
				SyntaxKind.ReactListKeyword => "ReactList",

				SyntaxKind.IfKeyword => "if",
				SyntaxKind.ElseKeyword => "else",
				SyntaxKind.ReturnKeyword => "return",
				SyntaxKind.ForKeyword => "for",

				SyntaxKind.TrueKeyword => "true",
				SyntaxKind.FalseKeyword => "false",
				SyntaxKind.NullKeyword => "null",

				SyntaxKind.PlusToken => "+",
				SyntaxKind.MinusToken => "-",
				SyntaxKind.AsteriskToken => "*",
				SyntaxKind.SlashToken => "/",
				SyntaxKind.PercentToken => "%",
				SyntaxKind.CaretToken => "^",
				SyntaxKind.BarToken => "|",
				SyntaxKind.AmpersandToken => "&",
				SyntaxKind.QuestionToken => "?",
				SyntaxKind.ColonToken => ":",
				SyntaxKind.SemicolonToken => ";",
				SyntaxKind.CommaToken => ",",
				SyntaxKind.DotToken => ".",
				SyntaxKind.DoubleDotToken => "..",
				SyntaxKind.EqualsToken => "=",
				SyntaxKind.BangToken => "!",
				SyntaxKind.EqualsEqualsToken => "==",
				SyntaxKind.BangEqualsToken => "!=",
				SyntaxKind.GreaterThanToken => ">",
				SyntaxKind.LessThanToken => "<",
				SyntaxKind.GreaterEqualsToken => ">=",
				SyntaxKind.LessEqualsToken => "<=",
				SyntaxKind.ArrowToken => "=>",
				SyntaxKind.HashToken => "#",

				SyntaxKind.OpenBraceToken => "{",
				SyntaxKind.CloseBraceToken => "}",
				SyntaxKind.OpenBracketToken => "[",
				SyntaxKind.CloseBracketToken => "]",
				SyntaxKind.OpenParenToken => "(",
				SyntaxKind.CloseParenToken => ")",
				SyntaxKind.DoubleColonToken => "::",

				SyntaxKind.LessSlashToken => "</",
				SyntaxKind.SlashGreaterToken => "/>",

				SyntaxKind.SingleQuoteToken => "'",
				SyntaxKind.DoubleQuoteToken => "\"",

				SyntaxKind.AtToken => "@",
				SyntaxKind.BindToken => "bind",
				SyntaxKind.InToken => "in",
				SyntaxKind.OutToken => "out",
				SyntaxKind.UsingKeyword => "using",
				SyntaxKind.NamespaceKeyword => "namespace",
				SyntaxKind.GlobalKeyword => "global",
				SyntaxKind.StaticKeyword => "static",
				SyntaxKind.UnsafeKeyword => "unsafe",

				SyntaxKind.UtilitiesKeyword => "utilities",
				SyntaxKind.AkcssKeyword => "akcss",
				SyntaxKind.ApplyKeyword => "apply",
				SyntaxKind.InterceptKeyword => "intercept",
				SyntaxKind.DollarToken => "$",

				_ => string.Empty,
			};
		}

		public static partial SyntaxKind GetKeywordKind(string text)
		{
			switch (text)
			{
				case "inject": return SyntaxKind.InjectKeyword;
				case "param": return SyntaxKind.ParamKeyword;
				case "state": return SyntaxKind.StateKeyword;
				case "suppress": return SyntaxKind.SuppressKeyword;
				case "finally": return SyntaxKind.FinallyKeyword;
				case "async": return SyntaxKind.AsyncKeyword;
				case "void": return SyntaxKind.VoidKeyword;
				case "command": return SyntaxKind.CommandKeyword;

				case "new": return SyntaxKind.NewKeyword;
				case "ReactList": return SyntaxKind.ReactListKeyword;

				case "if": return SyntaxKind.IfKeyword;
				case "else": return SyntaxKind.ElseKeyword;
				case "return": return SyntaxKind.ReturnKeyword;
				case "for": return SyntaxKind.ForKeyword;

				case "true": return SyntaxKind.TrueKeyword;
				case "false": return SyntaxKind.FalseKeyword;
				case "null": return SyntaxKind.NullKeyword;

				case "bind": return SyntaxKind.BindToken;
				case "in": return SyntaxKind.InToken;
				case "out": return SyntaxKind.OutToken;
				case "using": return SyntaxKind.UsingKeyword;
				case "namespace": return SyntaxKind.NamespaceKeyword;
				case "global": return SyntaxKind.GlobalKeyword;
				case "static": return SyntaxKind.StaticKeyword;
				case "unsafe": return SyntaxKind.UnsafeKeyword;

				case "utilities": return SyntaxKind.UtilitiesKeyword;
				case "akcss": return SyntaxKind.AkcssKeyword;
				case "apply": return SyntaxKind.ApplyKeyword;
				case "intercept": return SyntaxKind.InterceptKeyword;

				default:
					return SyntaxKind.None;
			}
		}

		public static partial SyntaxKind GetContextualKeywordKind(string text)
		{
			return SyntaxKind.None;
		}

		public static partial bool IsContextualKeyword(SyntaxKind kind)
		{
			return false;
		}

		public static partial object? GetValue(SyntaxKind kind, string text)
		{
			return kind switch
			{
				SyntaxKind.TrueKeyword => Boxes.BoxedTrue,
				SyntaxKind.FalseKeyword => Boxes.BoxedFalse,
				SyntaxKind.NullKeyword => null,
				_ => text,
			};
		}

		public static partial bool IsReservedKeyword(SyntaxKind kind)
		{
			switch (kind)
			{
				case SyntaxKind.InjectKeyword:
				case SyntaxKind.ParamKeyword:
				case SyntaxKind.StateKeyword:
				case SyntaxKind.SuppressKeyword:
				case SyntaxKind.FinallyKeyword:
				case SyntaxKind.AsyncKeyword:
				case SyntaxKind.VoidKeyword:
				case SyntaxKind.CommandKeyword:

				case SyntaxKind.NewKeyword:

				case SyntaxKind.IfKeyword:
				case SyntaxKind.ElseKeyword:
				case SyntaxKind.ReturnKeyword:
				case SyntaxKind.ForKeyword:

				case SyntaxKind.TrueKeyword:
				case SyntaxKind.FalseKeyword:
				case SyntaxKind.NullKeyword:

				case SyntaxKind.BindToken:
				case SyntaxKind.InToken:
				case SyntaxKind.OutToken:
				case SyntaxKind.UsingKeyword:
				case SyntaxKind.NamespaceKeyword:
				case SyntaxKind.GlobalKeyword:
				case SyntaxKind.StaticKeyword:
				case SyntaxKind.UnsafeKeyword:
				case SyntaxKind.UtilitiesKeyword:
				case SyntaxKind.AkcssKeyword:
				case SyntaxKind.ApplyKeyword:
				case SyntaxKind.InterceptKeyword:
					return true;

				default:
					return false;
			}
		}
	}
}

#nullable restore
