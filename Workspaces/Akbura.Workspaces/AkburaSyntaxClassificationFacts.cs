using Akbura.Language.Syntax;

namespace Akbura.Workspaces;

internal static class AkburaSyntaxClassificationFacts
{
    public static AkburaClassificationKind? GetClassification(
        SyntaxToken token)
    {
        if (token.Width == 0 || token.IsMissing)
        {
            return null;
        }

        return token.Kind switch
        {
            SyntaxKind.StringLiteralToken or
            SyntaxKind.CharLiteralToken =>
                AkburaClassificationKind.String,

            SyntaxKind.NumericLiteralToken =>
                AkburaClassificationKind.Number,

            SyntaxKind.AkTextLiteral =>
                AkburaClassificationKind.MarkupText,

            SyntaxKind.CSharpRawToken =>
                AkburaClassificationKind.EmbeddedCSharp,

            SyntaxKind.IdentifierToken =>
                GetIdentifierClassification(token),

            _ when IsKeyword(token.Kind) =>
                AkburaClassificationKind.Keyword,

            _ when IsOperator(token.Kind) =>
                AkburaClassificationKind.Operator,

            _ when IsDirectiveToken(token) =>
                AkburaClassificationKind.Directive,

            _ when IsPunctuation(token.Kind) =>
                AkburaClassificationKind.Punctuation,

            _ => null,
        };
    }

    public static AkburaClassificationKind? GetClassification(
        SyntaxTrivia trivia)
    {
        return trivia.Kind switch
        {
            SyntaxKind.SingleLineCommentTrivia or
            SyntaxKind.MultiLineCommentTrivia =>
                AkburaClassificationKind.Comment,

            _ => null,
        };
    }

    private static AkburaClassificationKind
        GetIdentifierClassification(SyntaxToken token)
    {
        var parentKind = token.Parent?.Kind ?? SyntaxKind.None;

        if (IsComponentName(parentKind))
        {
            return AkburaClassificationKind.Component;
        }

        if (IsAttribute(parentKind))
        {
            return AkburaClassificationKind.Attribute;
        }

        if (parentKind == SyntaxKind.CSharpTypeSyntax)
        {
            return AkburaClassificationKind.Type;
        }

        if (parentKind is
            SyntaxKind.NamespaceDeclarationSyntax or
            SyntaxKind.UsingDirectiveSyntax or
            SyntaxKind.UsingAliasSyntax)
        {
            return AkburaClassificationKind.Namespace;
        }

        return AkburaClassificationKind.Identifier;
    }

    private static bool IsComponentName(SyntaxKind kind)
    {
        return kind is
            SyntaxKind.MarkupComponentNameSyntax or
            SyntaxKind.MarkupSimpleComponentNameSyntax or
            SyntaxKind.MarkupQualifiedNameSyntax or
            SyntaxKind.MarkupAliasQualifierSyntax or
            SyntaxKind.MarkupQualifiedComponentNameSyntax or
            SyntaxKind.MarkupNameSegmentSyntax or
            SyntaxKind.MarkupIdentifierNameSegmentSyntax or
            SyntaxKind.MarkupGenericNameSegmentSyntax;
    }

    private static bool IsAttribute(SyntaxKind kind)
    {
        return kind is
            SyntaxKind.MarkupAttributeSyntax or
            SyntaxKind.MarkupPlainAttributeSyntax or
            SyntaxKind.MarkupAttachedPropertyAttributeSyntax or
            SyntaxKind.MarkupPrefixedAttributeSyntax or
            SyntaxKind.TailwindAttributeSyntax or
            SyntaxKind.TailwindFlagAttributeSyntax or
            SyntaxKind.TailwindFullAttributeSyntax;
    }

    private static bool IsDirectiveToken(SyntaxToken token)
    {
        if (token.Kind is SyntaxKind.AtToken or SyntaxKind.HashToken)
        {
            return true;
        }

        return token.Parent?.Kind is
            SyntaxKind.InlineAkcssBlockSyntax or
            SyntaxKind.AkcssApplyDirectiveSyntax or
            SyntaxKind.AkcssInterceptDirectiveSyntax or
            SyntaxKind.AkcssIfDirectiveSyntax;
    }

    private static bool IsKeyword(SyntaxKind kind)
    {
        return kind is
            SyntaxKind.InjectKeyword or
            SyntaxKind.ParamKeyword or
            SyntaxKind.StateKeyword or
            SyntaxKind.SuppressKeyword or
            SyntaxKind.FinallyKeyword or
            SyntaxKind.AsyncKeyword or
            SyntaxKind.VoidKeyword or
            SyntaxKind.CommandKeyword or
            SyntaxKind.NewKeyword or
            SyntaxKind.ReactListKeyword or
            SyntaxKind.IfKeyword or
            SyntaxKind.ElseKeyword or
            SyntaxKind.ReturnKeyword or
            SyntaxKind.ForKeyword or
            SyntaxKind.TrueKeyword or
            SyntaxKind.FalseKeyword or
            SyntaxKind.NullKeyword or
            SyntaxKind.BindToken or
            SyntaxKind.InToken or
            SyntaxKind.OutToken or
            SyntaxKind.UsingKeyword or
            SyntaxKind.NamespaceKeyword or
            SyntaxKind.GlobalKeyword or
            SyntaxKind.StaticKeyword or
            SyntaxKind.UnsafeKeyword or
            SyntaxKind.UtilitiesKeyword or
            SyntaxKind.AkcssKeyword or
            SyntaxKind.ApplyKeyword or
            SyntaxKind.InterceptKeyword;
    }

    private static bool IsOperator(SyntaxKind kind)
    {
        return kind is
            SyntaxKind.PlusToken or
            SyntaxKind.MinusToken or
            SyntaxKind.AsteriskToken or
            SyntaxKind.SlashToken or
            SyntaxKind.PercentToken or
            SyntaxKind.CaretToken or
            SyntaxKind.BarToken or
            SyntaxKind.AmpersandToken or
            SyntaxKind.QuestionToken or
            SyntaxKind.ColonToken or
            SyntaxKind.DoubleDotToken or
            SyntaxKind.EqualsToken or
            SyntaxKind.BangToken or
            SyntaxKind.EqualsEqualsToken or
            SyntaxKind.BangEqualsToken or
            SyntaxKind.GreaterThanToken or
            SyntaxKind.LessThanToken or
            SyntaxKind.GreaterEqualsToken or
            SyntaxKind.LessEqualsToken or
            SyntaxKind.ArrowToken;
    }

    private static bool IsPunctuation(SyntaxKind kind)
    {
        return kind is
            SyntaxKind.SemicolonToken or
            SyntaxKind.CommaToken or
            SyntaxKind.DotToken or
            SyntaxKind.OpenBraceToken or
            SyntaxKind.CloseBraceToken or
            SyntaxKind.OpenBracketToken or
            SyntaxKind.CloseBracketToken or
            SyntaxKind.OpenParenToken or
            SyntaxKind.CloseParenToken or
            SyntaxKind.DoubleColonToken or
            SyntaxKind.LessSlashToken or
            SyntaxKind.SlashGreaterToken or
            SyntaxKind.SingleQuoteToken or
            SyntaxKind.DoubleQuoteToken or
            SyntaxKind.DollarToken;
    }
}
