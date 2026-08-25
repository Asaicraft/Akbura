using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.Workspaces;

internal sealed class AkburaAutomaticPairService
{
    public static AkburaAutomaticPairService Instance { get; } = new();

    private AkburaAutomaticPairService()
    {
    }

    public AkburaPairDecision GetDecision(
        AkburaSyntacticDocument document,
        int position,
        char openingCharacter,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var context = document.GetPairContext(
            position,
            cancellationToken);
        if (context.IsDefault ||
            context.Kind is AkburaPairContextKind.Comment or
                AkburaPairContextKind.MarkupLiteralAttributeValue)
        {
            return AkburaPairDecision.None;
        }

        if (context.Kind == AkburaPairContextKind.EmbeddedCSharp)
        {
            return GetCSharpDecision(
                document,
                position,
                openingCharacter,
                context,
                cancellationToken);
        }

        var closingText = context.Kind switch
        {
            AkburaPairContextKind.MarkupText when openingCharacter == '<' => ">",
            AkburaPairContextKind.MarkupText when openingCharacter == '{' => "}",
            AkburaPairContextKind.MarkupStartTag when openingCharacter == '{' => "}",
            AkburaPairContextKind.MarkupStartTag when openingCharacter == '"' &&
                document.IsMarkupAttributeValueStart(position) => "\"",
            AkburaPairContextKind.MarkupExtension when openingCharacter == '{' => "}",
            AkburaPairContextKind.AkcssSyntax when openingCharacter == '{' => "}",
            AkburaPairContextKind.AkcssSyntax when openingCharacter == '(' &&
                document.CanStartAkcssParenthesizedConstruct(position) => ")",
            _ => string.Empty,
        };

        return closingText.Length == 0
            ? AkburaPairDecision.None
            : new AkburaPairDecision(
                context.Kind,
                openingCharacter,
                closingText);
    }

    private static AkburaPairDecision GetCSharpDecision(
        AkburaSyntacticDocument document,
        int position,
        char openingCharacter,
        AkburaPairContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!document.TryGetEmbeddedCSharpContext(
                position,
                out var embeddedContext,
                cancellationToken) ||
            !AkburaCSharpPairProjection.TryCreate(
                document,
                embeddedContext,
                openingCharacter,
                out var projection) ||
            projection.IsInsideComment)
        {
            return AkburaPairDecision.None;
        }

        var closingText = openingCharacter switch
        {
            '{' when projection.IsToken(SyntaxKind.OpenBraceToken) &&
                !projection.IsInterpolationBrace => "}",
            '(' when projection.IsToken(SyntaxKind.OpenParenToken) => ")",
            '[' when projection.IsToken(SyntaxKind.OpenBracketToken) => "]",
            '"' when projection.IsStringStart() => "\"",
            '<' when projection.IsGenericLessThan() => ">",
            _ => string.Empty,
        };
        if (closingText.Length == 0)
        {
            return AkburaPairDecision.None;
        }

        return new AkburaPairDecision(
            context.Kind,
            openingCharacter,
            closingText);
    }
}
