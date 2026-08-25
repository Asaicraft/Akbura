using Akbura.Language;
using Akbura.Language.Syntax;

namespace Akbura.Workspaces;

public sealed partial class AkburaSyntacticDocument
{
    internal AkburaPairDecision GetAutomaticPairDecision(
        int position,
        char openingCharacter,
        CancellationToken cancellationToken = default)
    {
        return AkburaAutomaticPairService.Instance.GetDecision(
            this,
            position,
            openingCharacter,
            cancellationToken);
    }

    internal bool ShouldAutoCloseCurlyBrace(
        int position,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        if (position == 0 ||
            Text[position - 1] != '{')
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var root = SyntaxTree.GetRootSyntax();
        if (root.FullWidth != Text.Length)
        {
            return false;
        }

        var bracePosition = position - 1;
        if (IsInsideComment(root, bracePosition))
        {
            return false;
        }

        var token = root.FindTokenInternal(bracePosition);
        if (token.Kind != SyntaxKind.OpenBraceToken ||
            token.Span.Start != bracePosition)
        {
            return false;
        }

        return token.Parent switch
        {
            AkcssStyleRuleSyntax rule =>
                rule.OpenBrace.Span == token.Span,
            AkcssUtilitiesSectionSyntax section =>
                section.OpenBrace.Span == token.Span,
            AkcssUtilityDeclarationSyntax utility =>
                utility.OpenBrace.Span == token.Span,
            AkcssIfDirectiveSyntax conditional =>
                conditional.OpenBrace.Span == token.Span,
            AkcssPseudoBlockSyntax pseudoBlock =>
                pseudoBlock.OpenBrace.Span == token.Span,
            InlineAkcssBlockSyntax block =>
                block.OpenBrace.Span == token.Span,
            _ => false,
        };
    }

    internal AkburaPairContext GetPairContext(
        int position,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        cancellationToken.ThrowIfCancellationRequested();

        var root = SyntaxTree.GetRootSyntax();
        if (root.FullWidth != Text.Length)
        {
            return default;
        }

        if (IsInsideComment(root, position))
        {
            return new AkburaPairContext(
                AkburaPairContextKind.Comment,
                position);
        }

        var isAkcss = TryGetAkcssRegion(position, out _);
        if (!isAkcss && IsInlineAkcssBlockStart(position))
        {
            return new AkburaPairContext(
                AkburaPairContextKind.AkcssSyntax,
                position,
                isAkcss: true);
        }

        if (TryGetEmbeddedCSharpContext(
                position,
                out var csharpContext,
                cancellationToken))
        {
            return new AkburaPairContext(
                AkburaPairContextKind.EmbeddedCSharp,
                position,
                isAkcss,
                csharpContext.Kind,
                csharpContext.HostSpan);
        }

        if (IsInsideMarkupLiteralValue(root, position))
        {
            return new AkburaPairContext(
                AkburaPairContextKind.MarkupLiteralAttributeValue,
                position);
        }

        if (isAkcss)
        {
            return new AkburaPairContext(
                AkburaPairContextKind.AkcssSyntax,
                position,
                isAkcss: true);
        }

        if (position > 0 &&
            Text[position - 1] == '$' &&
            IsInsideMarkupStartTagForPair(position))
        {
            return new AkburaPairContext(
                AkburaPairContextKind.MarkupExtension,
                position);
        }

        if (IsInsideMarkupStartTagForPair(position))
        {
            return new AkburaPairContext(
                AkburaPairContextKind.MarkupStartTag,
                position);
        }

        if (CanStartMarkupContent(root, position))
        {
            return new AkburaPairContext(
                AkburaPairContextKind.MarkupText,
                position);
        }

        return default;
    }

    internal bool IsMarkupAttributeValueStart(int position)
    {
        ValidatePosition(position);
        if (!IsInsideMarkupStartTagForPair(position))
        {
            return false;
        }

        var previous = GetPreviousNonWhitespace(position);
        return previous >= 0 && Text[previous] == '=';
    }

    internal bool CanStartAkcssParenthesizedConstruct(int position)
    {
        ValidatePosition(position);
        var previous = GetPreviousNonWhitespace(position);
        if (previous >= 0 && Text[previous] == '-')
        {
            return true;
        }

        var context = GetAkcssCompletionContext(position);
        if (context.Kind == AkcssCompletionContextKind.SelectorSnippet)
        {
            return true;
        }

        var line = Text.Lines.GetLineFromPosition(position);
        var prefix = Text.ToString(
                Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    line.Start,
                    position))
            .TrimEnd();
        return prefix.EndsWith("@if", StringComparison.Ordinal);
    }

    private bool IsInsideMarkupLiteralValue(
        AkburaSyntax root,
        int position)
    {
        if (Text.Length == 0)
        {
            return false;
        }

        if (position > 0 &&
            IsLiteralAncestorAt(root, position - 1))
        {
            return true;
        }

        if (position < Text.Length &&
            IsLiteralAncestorAt(root, position))
        {
            return true;
        }

        var tagStart = FindMarkupTagStartForPair(position);
        if (tagStart < 0)
        {
            return false;
        }

        var quote = '\0';
        for (var current = tagStart + 1;
             current < position;
             current++)
        {
            var character = Text[current];
            if (quote == '\0')
            {
                if (character is '\'' or '"')
                {
                    quote = character;
                }
                else if (character == '>')
                {
                    return false;
                }
            }
            else if (character == quote &&
                     (current == 0 || Text[current - 1] != '\\'))
            {
                quote = '\0';
            }
        }

        return quote != '\0';
    }

    private static bool IsLiteralAncestorAt(
        AkburaSyntax root,
        int position)
    {
        var token = root.FindTokenInternal(position);
        return FindAncestor<MarkupLiteralAttributeValueSyntax>(
            token.Parent) != null;
    }

    private bool IsInsideMarkupStartTagForPair(int position)
    {
        return FindMarkupTagStartForPair(position) >= 0;
    }

    private int FindMarkupTagStartForPair(int position)
    {
        var quote = '\0';
        for (var current = Math.Min(position - 1, Text.Length - 1);
             current >= 0;
             current--)
        {
            var character = Text[current];
            if (character is '\'' or '"')
            {
                quote = quote == '\0'
                    ? character
                    : quote == character
                        ? '\0'
                        : quote;
                continue;
            }

            if (quote != '\0')
            {
                continue;
            }

            if (character == '>')
            {
                return -1;
            }

            if (character != '<')
            {
                continue;
            }

            if (current + 1 < Text.Length &&
                Text[current + 1] is '/' or '!' or '?')
            {
                return -1;
            }

            return current;
        }

        return -1;
    }

    private bool CanStartMarkupContent(
        AkburaSyntax root,
        int position)
    {
        var completionContext = GetCompletionContext(position);
        if (completionContext.Kind ==
                AkburaCompletionContextKind.TopLevel &&
            completionContext.Prefix.Length == 0)
        {
            return true;
        }

        foreach (var element in root.DescendantNodes()
                     .OfType<MarkupElementSyntax>())
        {
            var startTag = element.StartTag;
            if (startTag == null ||
                startTag.CloseToken.IsMissing ||
                startTag.CloseToken.Kind == SyntaxKind.SlashGreaterToken ||
                startTag.CloseToken.Span.End > position)
            {
                continue;
            }

            var endTag = element.EndTag;
            if (endTag == null ||
                endTag.IsMissing ||
                position <= endTag.Span.Start)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInlineAkcssBlockStart(int position)
    {
        if (SyntaxTree.Kind == SyntaxTreeKind.Akcss)
        {
            return true;
        }

        var line = Text.Lines.GetLineFromPosition(position);
        var prefix = Text.ToString(
                Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    line.Start,
                    position))
            .TrimEnd();
        return prefix.EndsWith("@akcss", StringComparison.Ordinal);
    }

    private int GetPreviousNonWhitespace(int position)
    {
        for (var current = position - 1; current >= 0; current--)
        {
            if (!char.IsWhiteSpace(Text[current]))
            {
                return current;
            }
        }

        return -1;
    }
}
