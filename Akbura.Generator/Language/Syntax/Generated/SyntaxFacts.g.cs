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
                SyntaxKind.UseEffectKeyword => "useEffect",
                SyntaxKind.SuppressKeyword => "suppress",
                SyntaxKind.CancelKeyword => "cancel",
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
                SyntaxKind.LessSlashToken => "</",
                SyntaxKind.SlashGreaterToken => "/>",
                SyntaxKind.SingleQuoteToken => "'",
                SyntaxKind.DoubleQuoteToken => "\"",
                SyntaxKind.AtToken => "@",
                SyntaxKind.BindToken => "bind",
                SyntaxKind.InToken => "in",
                SyntaxKind.OutToken => "out",
                _ => ThrowHelper.ThrowArgumentException<string>($"Unexpected syntax kind: {kind}"),
            };
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
    }
}
#nullable restore