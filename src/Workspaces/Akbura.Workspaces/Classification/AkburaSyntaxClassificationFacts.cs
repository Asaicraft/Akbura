using Akbura.Language.Syntax;

namespace Akbura.Workspaces.Classification;

internal static class AkburaSyntaxClassificationFacts
{
    public static AkburaClassificationKind? GetClassification(SyntaxToken token)
    {
        if (token.Width == 0 || token.IsMissing)
        {
            return null;
        }

        var akcssClassification = GetAkcssClassification(token);

        if (akcssClassification is not null)
        {
            return akcssClassification;
        }

        if (token.Kind == SyntaxKind.CSharpRawToken)
        {
            return AkburaClassificationKind.EmbeddedCSharp;
        }

        var markupExtensionClassification = GetMarkupExtensionClassification(token);

        if (markupExtensionClassification is not null)
        {
            return markupExtensionClassification;
        }

        var utilityClassification =
            GetUtilityClassification(token);

        if (utilityClassification is not null)
        {
            return utilityClassification;
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

    private static AkburaClassificationKind? GetAkcssClassification(SyntaxToken token)
    {
        for (var node = token.Parent;
             node != null;
             node = node.Parent)
        {
            switch (node)
            {
                case InlineAkcssBlockSyntax block
                    when HasSameSpan(
                             block.AtToken,
                             token) ||
                         HasSameSpan(
                             block.AkcssKeyword,
                             token):

                    return AkburaClassificationKind
                        .Directive;

                case AkcssUsingDirectiveSyntax directive
                    when HasSameSpan(
                             directive.AtToken,
                             token) ||
                         HasSameSpan(
                             directive.UsingKeyword,
                             token):

                    return AkburaClassificationKind
                        .Directive;

                case AkcssUsingDirectiveSyntax directive
                    when Contains(
                        directive.Name,
                        token):

                    return AkburaClassificationKind
                        .Namespace;

                case AkcssUtilitiesSectionSyntax section
                    when HasSameSpan(
                             section.AtToken,
                             token) ||
                         HasSameSpan(
                             section.UtilitiesToken,
                             token):

                    return AkburaClassificationKind
                        .Directive;

                case AkcssApplyDirectiveSyntax apply
                    when HasSameSpan(
                             apply.AtToken,
                             token) ||
                         HasSameSpan(
                             apply.ApplyKeyword,
                             token):

                    return AkburaClassificationKind
                        .Directive;

                case AkcssInterceptDirectiveSyntax intercept
                    when HasSameSpan(
                             intercept.AtToken,
                             token) ||
                         HasSameSpan(
                             intercept.InterceptKeyword,
                             token):

                    return AkburaClassificationKind
                        .Directive;

                case AkcssIfDirectiveSyntax conditional
                    when HasSameSpan(
                             conditional.AtToken,
                             token) ||
                         HasSameSpan(
                             conditional.IfKeyword,
                             token):

                    return AkburaClassificationKind
                        .Directive;

                case AkcssStyleSelectorSyntax selector
                    when selector.Name is { } name &&
                         Contains(
                             name,
                             token):

                    return AkburaClassificationKind
                        .Utility;

                case AkcssUtilitySelectorSyntax selector
                    when Contains(
                        selector.Name,
                        token):

                    return AkburaClassificationKind
                        .Utility;

                case AkcssUtilityParameterSyntax parameter
                    when Contains(
                        parameter.ParamName,
                        token):

                    return AkburaClassificationKind
                        .ParameterName;

                case AkcssAssignmentSyntax assignment
                    when Contains(
                        assignment.PropertyName,
                        token):

                    return token.Kind ==
                               SyntaxKind.IdentifierToken &&
                           token.Span.End ==
                               assignment
                                   .PropertyName
                                   .Span.End
                        ? AkburaClassificationKind
                            .PropertyName
                        : AkburaClassificationKind
                            .Identifier;

                case AkcssAdditionalPseudoStateSyntax state
                    when HasSameSpan(
                             state.AtToken,
                             token) ||
                         Contains(
                             state.State,
                             token):

                    return AkburaClassificationKind
                        .UtilityModifier;

                case AkcssPseudoSelectorSyntax selector
                    when HasSameSpan(
                             selector.AtToken,
                             token) ||
                         Contains(
                             selector.FirstState,
                             token):

                    return AkburaClassificationKind
                        .UtilityModifier;
            }
        }

        return null;
    }

    private static AkburaClassificationKind? GetMarkupExtensionClassification(SyntaxToken token)
    {
        if (!IsInsideMarkupExtension(token))
        {
            return null;
        }

        if (token.Kind is
            SyntaxKind.DollarToken or
            SyntaxKind.OpenBraceToken or
            SyntaxKind.CloseBraceToken or
            SyntaxKind.CommaToken or
            SyntaxKind.DotToken or
            SyntaxKind.DoubleColonToken or
            SyntaxKind.EqualsToken)
        {
            return AkburaClassificationKind
                .MarkupExtensionPunctuation;
        }

        for (var node = token.Parent;
             node is not null;
             node = node.Parent)
        {
            switch (node)
            {
                case MarkupExtensionTypeSyntax:
                    return AkburaClassificationKind
                        .MarkupExtensionType;

                case MarkupExtensionPropertyArgumentSyntax property
                    when Contains(property.Name, token):

                    return AkburaClassificationKind
                        .MarkupExtensionProperty;

                case MarkupExtensionLiteralValueSyntax:
                    return AkburaClassificationKind
                        .MarkupExtensionValue;

                case MarkupExtensionSyntax:
                    return token.Kind == SyntaxKind.IdentifierToken
                        ? AkburaClassificationKind.MarkupExtensionValue
                        : null;
            }
        }

        return null;
    }

    private static bool IsInsideMarkupExtension(SyntaxToken token)
    {
        for (var node = token.Parent;
             node is not null;
             node = node.Parent)
        {
            if (node is MarkupExtensionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    public static AkburaClassificationKind? GetClassification(SyntaxTrivia trivia)
    {
        return trivia.Kind switch
        {
            SyntaxKind.SingleLineCommentTrivia or
            SyntaxKind.MultiLineCommentTrivia =>
                AkburaClassificationKind.Comment,

            _ => null,
        };
    }

    private static AkburaClassificationKind? GetUtilityClassification(SyntaxToken token)
    {
        for (var node = token.Parent;
             node is not null;
             node = node.Parent)
        {
            if (node is TailwindPrefixSegmentSyntax)
            {
                return AkburaClassificationKind.UtilityModifier;
            }

            if (node is TailwindAttributeSyntax)
            {
                return AkburaClassificationKind.Utility;
            }
        }

        return null;
    }

    private static AkburaClassificationKind
        GetIdentifierClassification(SyntaxToken token)
    {
        for (var node = token.Parent;
             node is not null;
             node = node.Parent)
        {
            switch (node)
            {
                // <Button>
                // <Router.NotFound>
                //
                case MarkupComponentNameSyntax componentName:
                    return componentName.Parent is
                        MarkupAttachedPropertyAttributeSyntax
                            ? AkburaClassificationKind.Type
                            : AkburaClassificationKind.Component;

                // Header="..."
                case MarkupPlainAttributeSyntax attribute
                    when Contains(attribute.Name, token):

                    return AkburaClassificationKind.Attribute;

                // Grid.Row="..."
                case MarkupAttachedPropertyAttributeSyntax attribute
                    when Contains(attribute.Name, token):

                    return AkburaClassificationKind.Attribute;

                // bind:Text="..."
                // out:Value="..."
                //
                // bind/out 
                // а Text/Value
                case MarkupPrefixedAttributeSyntax attribute
                    when Contains(attribute.Name, token):

                    return AkburaClassificationKind.Attribute;

                case CSharpTypeSyntax:
                    return AkburaClassificationKind.Type;

                case NamespaceDeclarationSyntax:
                case UsingDirectiveSyntax:
                case UsingAliasSyntax:
                    return AkburaClassificationKind.Namespace;
            }
        }

        return AkburaClassificationKind.Identifier;
    }

    private static bool Contains(
        AkburaSyntax syntax,
        SyntaxToken token)
    {
        return syntax.FullSpan.Contains(token.Span);
    }

    private static bool IsDirectiveToken(SyntaxToken token)
    {
        return token.Kind is
            SyntaxKind.AtToken or
            SyntaxKind.HashToken;
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

    private static bool HasSameSpan(SyntaxToken left, SyntaxToken right)
    {
        return left.Span == right.Span;
    }
}
