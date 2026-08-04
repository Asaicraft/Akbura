using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using AkburaSyntaxToken =
    Akbura.Language.Syntax.SyntaxToken;
using CSharpSyntaxKind =
    Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxToken =
    Microsoft.CodeAnalysis.SyntaxToken;
using CSharpSyntaxTrivia =
    Microsoft.CodeAnalysis.SyntaxTrivia;

namespace Akbura.Workspaces;

internal sealed class EmbeddedCSharpClassificationService
{
    public bool TryAddClassifications(
        AkburaSyntaxToken token,
        TextSpan requestedSpan,
        ImmutableArray<AkburaClassifiedSpan>.Builder builder,
        CancellationToken cancellationToken)
    {
        if (token.Node is not
                GreenSyntaxToken.CSharpRawToken rawToken ||
            rawToken.RawNode is not { } rawNode)
        {
            return false;
        }

        if (rawNode.FullSpan.Length != token.Span.Length)
        {
            return false;
        }

        var positionOffset =
            token.Span.Start -
            rawNode.FullSpan.Start;

        foreach (var csharpToken in rawNode.DescendantTokens())
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddTrivia(
                csharpToken.LeadingTrivia,
                positionOffset,
                requestedSpan,
                builder,
                cancellationToken);

            AddToken(
                csharpToken,
                positionOffset,
                requestedSpan,
                builder);

            AddTrivia(
                csharpToken.TrailingTrivia,
                positionOffset,
                requestedSpan,
                builder,
                cancellationToken);
        }

        return true;
    }

    private static void AddToken(
        CSharpSyntaxToken token,
        int positionOffset,
        TextSpan requestedSpan,
        ImmutableArray<AkburaClassifiedSpan>.Builder builder)
    {
        var classification = GetClassification(token);

        if (classification is null)
        {
            return;
        }

        AddMappedSpan(
            token.Span,
            positionOffset,
            requestedSpan,
            classification.Value,
            builder);
    }

    private static void AddTrivia(
        SyntaxTriviaList triviaList,
        int positionOffset,
        TextSpan requestedSpan,
        ImmutableArray<AkburaClassifiedSpan>.Builder builder,
        CancellationToken cancellationToken)
    {
        foreach (var trivia in triviaList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classification = GetClassification(trivia);

            if (classification is null)
            {
                continue;
            }

            AddMappedSpan(
                trivia.Span,
                positionOffset,
                requestedSpan,
                classification.Value,
                builder);
        }
    }

    private static void AddMappedSpan(
        TextSpan csharpSpan,
        int positionOffset,
        TextSpan requestedSpan,
        AkburaClassificationKind classification,
        ImmutableArray<AkburaClassifiedSpan>.Builder builder)
    {
        if (csharpSpan.Length == 0)
        {
            return;
        }

        var akburaSpan = new TextSpan(
            csharpSpan.Start + positionOffset,
            csharpSpan.Length);

        if (!akburaSpan.OverlapsWith(requestedSpan))
        {
            return;
        }

        builder.Add(new AkburaClassifiedSpan(
            akburaSpan,
            classification));
    }

    private static AkburaClassificationKind?
        GetClassification(CSharpSyntaxToken token)
    {
        var kind = token.Kind();

        if (SyntaxFacts.IsKeywordKind(kind) ||
            SyntaxFacts.IsContextualKeyword(kind))
        {
            return AkburaClassificationKind.Keyword;
        }

        return kind switch
        {
            CSharpSyntaxKind.IdentifierToken =>
                AkburaClassificationKind.Identifier,

            CSharpSyntaxKind.NumericLiteralToken =>
                AkburaClassificationKind.Number,

            CSharpSyntaxKind.StringLiteralToken or
            CSharpSyntaxKind.Utf8StringLiteralToken or
            CSharpSyntaxKind.CharacterLiteralToken or
            CSharpSyntaxKind.SingleLineRawStringLiteralToken or
            CSharpSyntaxKind.MultiLineRawStringLiteralToken or
            CSharpSyntaxKind.Utf8SingleLineRawStringLiteralToken or
            CSharpSyntaxKind.Utf8MultiLineRawStringLiteralToken or
            CSharpSyntaxKind.InterpolatedStringTextToken or
            CSharpSyntaxKind.InterpolatedStringStartToken or
            CSharpSyntaxKind.InterpolatedVerbatimStringStartToken or
            CSharpSyntaxKind.InterpolatedStringEndToken =>
                AkburaClassificationKind.String,

            _ when IsOperator(kind) =>
                AkburaClassificationKind.Operator,

            _ when IsPunctuation(kind) =>
                AkburaClassificationKind.Punctuation,

            _ => null,
        };
    }

    private static AkburaClassificationKind?
        GetClassification(CSharpSyntaxTrivia trivia)
    {
        return trivia.Kind() switch
        {
            CSharpSyntaxKind.SingleLineCommentTrivia or
            CSharpSyntaxKind.MultiLineCommentTrivia or
            CSharpSyntaxKind.SingleLineDocumentationCommentTrivia or
            CSharpSyntaxKind.MultiLineDocumentationCommentTrivia or
            CSharpSyntaxKind.DocumentationCommentExteriorTrivia or
            CSharpSyntaxKind.DisabledTextTrivia =>
                AkburaClassificationKind.Comment,

            _ => null,
        };
    }

    private static bool IsOperator(CSharpSyntaxKind kind)
    {
        return kind is
            CSharpSyntaxKind.PlusToken or
            CSharpSyntaxKind.MinusToken or
            CSharpSyntaxKind.AsteriskToken or
            CSharpSyntaxKind.SlashToken or
            CSharpSyntaxKind.PercentToken or
            CSharpSyntaxKind.AmpersandToken or
            CSharpSyntaxKind.BarToken or
            CSharpSyntaxKind.CaretToken or
            CSharpSyntaxKind.ExclamationToken or
            CSharpSyntaxKind.TildeToken or
            CSharpSyntaxKind.EqualsToken or
            CSharpSyntaxKind.LessThanToken or
            CSharpSyntaxKind.GreaterThanToken or
            CSharpSyntaxKind.LessThanEqualsToken or
            CSharpSyntaxKind.GreaterThanEqualsToken or
            CSharpSyntaxKind.EqualsEqualsToken or
            CSharpSyntaxKind.ExclamationEqualsToken or
            CSharpSyntaxKind.AmpersandAmpersandToken or
            CSharpSyntaxKind.BarBarToken or
            CSharpSyntaxKind.PlusPlusToken or
            CSharpSyntaxKind.MinusMinusToken or
            CSharpSyntaxKind.QuestionQuestionToken or
            CSharpSyntaxKind.QuestionQuestionEqualsToken or
            CSharpSyntaxKind.PlusEqualsToken or
            CSharpSyntaxKind.MinusEqualsToken or
            CSharpSyntaxKind.AsteriskEqualsToken or
            CSharpSyntaxKind.SlashEqualsToken or
            CSharpSyntaxKind.PercentEqualsToken or
            CSharpSyntaxKind.AmpersandEqualsToken or
            CSharpSyntaxKind.BarEqualsToken or
            CSharpSyntaxKind.CaretEqualsToken or
            CSharpSyntaxKind.LessThanLessThanToken or
            CSharpSyntaxKind.GreaterThanGreaterThanToken or
            CSharpSyntaxKind.LessThanLessThanEqualsToken or
            CSharpSyntaxKind.GreaterThanGreaterThanEqualsToken or
            CSharpSyntaxKind.EqualsGreaterThanToken or
            CSharpSyntaxKind.MinusGreaterThanToken or
            CSharpSyntaxKind.QuestionToken;
    }

    private static bool IsPunctuation(CSharpSyntaxKind kind)
    {
        return kind is
            CSharpSyntaxKind.OpenParenToken or
            CSharpSyntaxKind.CloseParenToken or
            CSharpSyntaxKind.OpenBraceToken or
            CSharpSyntaxKind.CloseBraceToken or
            CSharpSyntaxKind.OpenBracketToken or
            CSharpSyntaxKind.CloseBracketToken or
            CSharpSyntaxKind.CommaToken or
            CSharpSyntaxKind.DotToken or
            CSharpSyntaxKind.SemicolonToken or
            CSharpSyntaxKind.ColonToken or
            CSharpSyntaxKind.ColonColonToken or
            CSharpSyntaxKind.HashToken;
    }
}