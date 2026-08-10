using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

public sealed partial class AkburaSyntacticDocument
{
    /// <summary>
    /// Determines the completion construct at <paramref name="position"/>.
    /// </summary>
    public AkburaSyntacticCompletionContext GetCompletionContext(
        int position,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        if (SyntaxTree.Kind == SyntaxTreeKind.Akcss ||
            position == 0 || Text.Length == 0)
        {
            return default;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var root = SyntaxTree.GetRootSyntax();
        if (TryGetIncompleteClosingTagContext(
                root,
                position,
                out var incompleteClosingContext))
        {
            return incompleteClosingContext;
        }

        var token = root.FindTokenInternal(
            Math.Min(position - 1, Text.Length - 1));

        var endTag = FindAncestor<MarkupEndTagSyntax>(
            token.Parent);
        if (endTag != null &&
            IsBeforeTagClose(
                position,
                endTag.GreaterToken))
        {
            var applicableSpan = GetApplicableNameSpan(
                position,
                endTag.LessSlashToken.Span.End);
            return new AkburaSyntacticCompletionContext(
                AkburaCompletionContextKind.ClosingComponentName,
                applicableSpan,
                Text.ToString(applicableSpan),
                componentName: null,
                parentComponentName:
                    GetClosingTagParentName(
                        endTag,
                        position),
                ImmutableArray<string>.Empty);
        }

        var startTag = FindAncestor<MarkupStartTagSyntax>(
            token.Parent);
        if (startTag == null ||
            position < startTag.LessToken.Span.End ||
            !IsBeforeTagClose(
                position,
                startTag.CloseToken))
        {
            return default;
        }

        if (IsInsideAttributeValue(
                token.Parent,
                startTag,
                position))
        {
            return default;
        }

        var element = startTag.Parent as MarkupElementSyntax;
        var componentName = startTag.Name
            .ToFullString()
            .Trim();
        var nameEndsAt = startTag.Name.Span.End;
        var isComponentName =
            startTag.Name.IsMissing ||
            position <= nameEndsAt;

        if (isComponentName)
        {
            var applicableSpan = GetApplicableNameSpan(
                position,
                startTag.LessToken.Span.End);
            var parentName = GetParentElementName(element);
            var kind = parentName != null &&
                Text.ToString(applicableSpan)
                    .IndexOf(".", StringComparison.Ordinal) >= 0
                    ? AkburaCompletionContextKind.PropertyElementName
                    : AkburaCompletionContextKind.ComponentName;

            return new AkburaSyntacticCompletionContext(
                kind,
                applicableSpan,
                Text.ToString(applicableSpan),
                componentName: kind ==
                    AkburaCompletionContextKind.PropertyElementName
                        ? parentName
                        : null,
                parentComponentName: parentName,
                ImmutableArray<string>.Empty);
        }

        var attributeSpan = GetApplicableNameSpan(
            position,
            Math.Max(
                startTag.Name.Span.End,
                startTag.LessToken.Span.End));
        return new AkburaSyntacticCompletionContext(
            AkburaCompletionContextKind.AttributeName,
            attributeSpan,
            Text.ToString(attributeSpan),
            componentName,
            parentComponentName: GetParentElementName(element),
            GetExistingAttributeNames(startTag));
    }

    /// <summary>
    /// Returns a closing tag that should be inserted after a newly typed
    /// <c>&gt;</c>, or <see langword="null"/> when no insertion is needed.
    /// </summary>
    public string? GetAutoClosingTagText(
        int position,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        if (SyntaxTree.Kind == SyntaxTreeKind.Akcss ||
            position == 0 || Text.Length == 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Text[position - 1] != '>')
        {
            return null;
        }

        var root = SyntaxTree.GetRootSyntax();
        var startTag = root
            .DescendantNodes()
            .OfType<MarkupStartTagSyntax>()
            .LastOrDefault(candidate =>
                !candidate.CloseToken.IsMissing &&
                candidate.CloseToken.Kind ==
                    SyntaxKind.GreaterThanToken &&
                candidate.CloseToken.Span.End == position);
        if (startTag != null && !startTag.Name.IsMissing)
        {
            var syntaxName = startTag.Name.ToFullString().Trim();
            return syntaxName.Length == 0
                ? null
                : HasMatchingClosingTagAfter(
                    root,
                    position,
                    syntaxName)
                    ? null
                    : $"</{syntaxName}>";
        }

        if (!TryGetStartTagNameEndingAt(position, out var name) ||
            HasMatchingClosingTagAfter(root, position, name))
        {
            return null;
        }

        return $"</{name}>";
    }

    private void ValidatePosition(int position)
    {
        if ((uint)position > (uint)Text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position));
        }
    }

    private TextSpan GetApplicableNameSpan(
        int position,
        int minimumStart)
    {
        var start = position;
        while (start > minimumStart &&
               IsCompletionNameCharacter(
                   Text[start - 1]))
        {
            start--;
        }

        return TextSpan.FromBounds(start, position);
    }

    private bool TryGetIncompleteClosingTagContext(
        AkburaSyntax root,
        int position,
        out AkburaSyntacticCompletionContext context)
    {
        var nameStart = position;
        while (nameStart > 0 &&
               IsCompletionNameCharacter(Text[nameStart - 1]))
        {
            nameStart--;
        }

        if (nameStart < 2 ||
            Text[nameStart - 2] != '<' ||
            Text[nameStart - 1] != '/')
        {
            context = default;
            return false;
        }

        var token = root.FindTokenInternal(
            Math.Min(position - 1, Text.Length - 1));
        if (FindAncestor<MarkupAttributeValueSyntax>(token.Parent) != null)
        {
            context = default;
            return false;
        }

        var applicableSpan = TextSpan.FromBounds(
            nameStart,
            position);
        context = new AkburaSyntacticCompletionContext(
            AkburaCompletionContextKind.ClosingComponentName,
            applicableSpan,
            Text.ToString(applicableSpan),
            componentName: null,
            parentComponentName: GetOpenElementName(root, nameStart - 2),
            ImmutableArray<string>.Empty);
        return true;
    }

    private static bool IsCompletionNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value) ||
            value is '_' or '-' or '.' or ':';
    }

    private bool TryGetStartTagNameEndingAt(
        int position,
        out string name)
    {
        name = string.Empty;
        var lessPosition = position - 2;
        var quote = '\0';
        while (lessPosition >= 0)
        {
            var character = Text[lessPosition];
            if (character is '\'' or '"')
            {
                quote = quote == '\0'
                    ? character
                    : quote == character
                        ? '\0'
                        : quote;
            }
            else if (character == '<' && quote == '\0')
            {
                break;
            }
            else if (character == '>' && quote == '\0')
            {
                return false;
            }

            lessPosition--;
        }

        if (lessPosition < 0 || quote != '\0')
        {
            return false;
        }

        var contentStart = lessPosition + 1;
        if (contentStart >= position - 1 ||
            Text[contentStart] is '/' or '!' or '?' ||
            Text[position - 2] == '/')
        {
            return false;
        }

        var nameEnd = contentStart;
        while (nameEnd < position - 1 &&
               !char.IsWhiteSpace(Text[nameEnd]) &&
               Text[nameEnd] != '/')
        {
            nameEnd++;
        }

        if (nameEnd == contentStart)
        {
            return false;
        }

        name = Text.ToString(TextSpan.FromBounds(
            contentStart,
            nameEnd));
        return true;
    }

    private static bool HasMatchingClosingTagAfter(
        AkburaSyntax root,
        int position,
        string name)
    {
        var depth = 0;
        foreach (var node in root.DescendantNodes()
                     .Where(node => node.Span.Start >= position)
                     .OrderBy(node => node.Span.Start))
        {
            if (node is MarkupStartTagSyntax startTag &&
                startTag.CloseToken.Kind !=
                    SyntaxKind.SlashGreaterToken &&
                string.Equals(
                    startTag.Name.ToFullString().Trim(),
                    name,
                    StringComparison.Ordinal))
            {
                depth++;
            }
            else if (node is MarkupEndTagSyntax endTag &&
                     string.Equals(
                         endTag.Name.ToFullString().Trim(),
                         name,
                         StringComparison.Ordinal))
            {
                if (depth == 0)
                {
                    return true;
                }

                depth--;
            }
        }

        return false;
    }

    private static bool IsBeforeTagClose(
        int position,
        SyntaxToken closeToken)
    {
        return closeToken.IsMissing ||
            position <= closeToken.Span.Start;
    }

    private static bool IsInsideAttributeValue(
        AkburaSyntax? node,
        MarkupStartTagSyntax startTag,
        int position)
    {
        for (var current = node;
             current != null &&
             !ReferenceEquals(current, startTag);
             current = current.Parent)
        {
            if (current is MarkupAttributeValueSyntax)
            {
                return true;
            }

            if (current is MarkupPlainAttributeSyntax plain &&
                !plain.EqualsToken.IsMissing &&
                position >= plain.EqualsToken.Span.End)
            {
                return true;
            }

            if (current is MarkupAttachedPropertyAttributeSyntax attached &&
                !attached.EqualsToken.IsMissing &&
                position >= attached.EqualsToken.Span.End)
            {
                return true;
            }

            if (current is MarkupPrefixedAttributeSyntax prefixed &&
                !prefixed.EqualsToken.IsMissing &&
                position >= prefixed.EqualsToken.Span.End)
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<string> GetExistingAttributeNames(
        MarkupStartTagSyntax startTag)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var attribute in startTag.Attributes)
        {
            var name = attribute switch
            {
                MarkupPlainAttributeSyntax plain =>
                    plain.Name.ToFullString().Trim(),
                MarkupAttachedPropertyAttributeSyntax attached =>
                    attached.OwnerType.ToFullString().Trim() +
                    "." +
                    attached.Name.ToFullString().Trim(),
                MarkupPrefixedAttributeSyntax prefixed =>
                    prefixed.Prefix.ToString() +
                    ":" +
                    prefixed.Name.ToFullString().Trim(),
                _ => attribute.ToFullString().Trim(),
            };

            if (name.Length > 0)
            {
                builder.Add(name);
            }
        }

        return builder.ToImmutable();
    }

    private static string? GetClosingTagParentName(
        MarkupEndTagSyntax endTag,
        int position)
    {
        if (endTag.Parent is MarkupElementSyntax element &&
            element.StartTag is { } startTag)
        {
            var name = startTag.Name.ToFullString().Trim();
            if (name.Length > 0)
            {
                return name;
            }
        }

        return GetOpenElementName(endTag.Root, position);
    }

    private static string? GetParentElementName(
        MarkupElementSyntax? element)
    {
        if (element == null)
        {
            return null;
        }

        for (var current = element.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is MarkupElementSyntax parent &&
                parent.StartTag is { } startTag)
            {
                var name = startTag.Name.ToFullString().Trim();
                return name.Length == 0 ? null : name;
            }
        }

        return null;
    }

    private static string? GetOpenElementName(
        AkburaSyntax root,
        int position)
    {
        MarkupStartTagSyntax? best = null;
        foreach (var element in root
                     .DescendantNodes()
                     .OfType<MarkupElementSyntax>())
        {
            var startTag = element.StartTag;
            if (startTag == null ||
                startTag.CloseToken.IsMissing ||
                startTag.CloseToken.Span.End > position ||
                HasCompleteEndTagBefore(
                    element.EndTag,
                    position))
            {
                continue;
            }

            if (best == null ||
                startTag.Span.Start > best.Span.Start)
            {
                best = startTag;
            }
        }

        var name = best?.Name.ToFullString().Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static bool HasCompleteEndTag(
        MarkupEndTagSyntax? endTag)
    {
        return endTag != null &&
            !endTag.IsMissing &&
            !endTag.LessSlashToken.IsMissing &&
            !endTag.GreaterToken.IsMissing &&
            !endTag.Name.IsMissing;
    }

    private static bool HasCompleteEndTagBefore(
        MarkupEndTagSyntax? endTag,
        int position)
    {
        return HasCompleteEndTag(endTag) &&
            endTag!.Span.End <= position;
    }

    private static TNode? FindAncestor<TNode>(
        AkburaSyntax? node)
        where TNode : AkburaSyntax
    {
        for (var current = node;
             current != null;
             current = current.Parent)
        {
            if (current is TNode result)
            {
                return result;
            }
        }

        return null;
    }
}
