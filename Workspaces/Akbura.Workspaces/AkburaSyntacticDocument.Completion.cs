using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Pools;
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
        if (SyntaxTree.Kind == SyntaxTreeKind.Akcss)
        {
            return default;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var root = SyntaxTree.GetRootSyntax();
        if (TryGetDeclarationModifierContext(
                root,
                position,
                out var declarationModifierContext))
        {
            return declarationModifierContext;
        }

        if (TryGetMarkupExtensionTypeContext(
                root,
                position,
                out var markupExtensionContext))
        {
            return markupExtensionContext;
        }

        if (TryGetIncompleteClosingTagContext(
                root,
                position,
                out var incompleteClosingContext))
        {
            return incompleteClosingContext;
        }

        if (position == 0 || Text.Length == 0)
        {
            return TryGetTopLevelKeywordContext(
                root,
                position,
                out var emptyTopLevelContext)
                    ? emptyTopLevelContext
                    : default;
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

        var startTag = GetStartTagAtPosition(
            root,
            token.Parent,
            position);
        if (startTag == null ||
            position < startTag.LessToken.Span.End ||
            !IsBeforeStartTagClose(
                position,
                startTag))
        {
            return TryGetTopLevelKeywordContext(
                root,
                position,
                out var topLevelContext)
                    ? topLevelContext
                    : default;
        }

        if (IsInsideAttributeValue(
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

        var attributeSpan = GetApplicableAttributeNameSpan(
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

        if (TryGetEmbeddedCSharpContext(
                position - 1,
                out _,
                cancellationToken))
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

    /// <summary>
    /// Returns text that should be inserted after a newly typed
    /// <c>/</c>, or <see langword="null"/> when no insertion is needed.
    /// </summary>
    public string? GetSlashCompletionText(
        int position,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        if (SyntaxTree.Kind == SyntaxTreeKind.Akcss ||
            position == 0 ||
            Text.Length == 0 ||
            Text[position - 1] != '/')
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var slashPosition = position - 1;
        if (TryGetEmbeddedCSharpContext(
                slashPosition,
                out _,
                cancellationToken))
        {
            return null;
        }

        if (slashPosition > 0 &&
            Text[slashPosition - 1] == '<')
        {
            var closingTagContext = GetCompletionContext(
                position,
                cancellationToken);
            if (closingTagContext.Kind !=
                    AkburaCompletionContextKind
                        .ClosingComponentName ||
                closingTagContext.Prefix.Length != 0 ||
                string.IsNullOrWhiteSpace(
                    closingTagContext.ParentComponentName))
            {
                return null;
            }

            return closingTagContext.ParentComponentName + ">";
        }

        var context = GetCompletionContext(
            slashPosition,
            cancellationToken);
        var afterComponentName =
            (context.Kind ==
                 AkburaCompletionContextKind.ComponentName ||
             context.Kind ==
                 AkburaCompletionContextKind.PropertyElementName) &&
            context.ApplicableSpan.End == slashPosition &&
            context.Prefix.Length > 0;
        var afterAttributeBoundary =
            context.Kind ==
                AkburaCompletionContextKind.AttributeName &&
            context.Prefix.Length == 0 &&
            !string.IsNullOrWhiteSpace(
                context.ComponentName);
        if (!afterComponentName &&
            !afterAttributeBoundary)
        {
            return null;
        }

        if (position < Text.Length &&
            Text[position] == '>')
        {
            return null;
        }

        return ">";
    }

    /// <summary>
    /// Returns the structural indentation level for a closing tag completed
    /// after <c>&lt;/</c>, or <see langword="null"/> for other slash uses.
    /// </summary>
    public int? GetSlashCompletionIndentationLevel(
        int position,
        CancellationToken cancellationToken = default)
    {
        var completionText = GetSlashCompletionText(
            position,
            cancellationToken);
        if (completionText == null ||
            string.Equals(
                completionText,
                ">",
                StringComparison.Ordinal))
        {
            return null;
        }

        var root = SyntaxTree.GetRootSyntax();
        var startTag = GetOpenElementStartTag(
            root,
            position - 2);
        return startTag == null
            ? null
            : GetDesiredIndentationLevelAtPosition(
                startTag.Span.Start,
                cancellationToken);
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

    private TextSpan GetApplicableAttributeNameSpan(
        int position,
        int minimumStart)
    {
        var span = GetApplicableNameSpan(
            position,
            minimumStart);
        if (span.Length > 0 &&
            Text[span.Start] == ':' &&
            span.Start > minimumStart &&
            Text[span.Start - 1] == '}')
        {
            return TextSpan.FromBounds(
                span.Start + 1,
                span.End);
        }

        return span;
    }

    private bool TryGetMarkupExtensionTypeContext(
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

        var openBracePosition = nameStart - 1;
        while (openBracePosition >= 0 &&
               char.IsWhiteSpace(Text[openBracePosition]))
        {
            openBracePosition--;
        }

        if (openBracePosition < 1 ||
            Text[openBracePosition] != '{' ||
            Text[openBracePosition - 1] != '$')
        {
            context = default;
            return false;
        }

        var token = root.FindTokenInternal(
            Math.Min(position - 1, Text.Length - 1));
        var extension = FindAncestor<MarkupExtensionSyntax>(
            token.Parent);
        if ((extension == null ||
             extension.OpenBrace.Span.Start != openBracePosition) &&
            !IsInsideMarkupStartTag(openBracePosition - 1))
        {
            context = default;
            return false;
        }

        var applicableSpan = TextSpan.FromBounds(
            nameStart,
            position);
        context = new AkburaSyntacticCompletionContext(
            AkburaCompletionContextKind.MarkupExtensionType,
            applicableSpan,
            Text.ToString(applicableSpan),
            componentName: null,
            parentComponentName: null,
            ImmutableArray<string>.Empty);
        return true;
    }

    private bool IsInsideMarkupStartTag(int position)
    {
        return GetMarkupStartTagStart(position) >= 0;
    }

    private MarkupStartTagSyntax? GetStartTagAtPosition(
        AkburaSyntax root,
        AkburaSyntax? node,
        int position)
    {
        var startTag = FindAncestor<MarkupStartTagSyntax>(node);
        if (startTag != null)
        {
            return startTag;
        }

        var tagStart = GetMarkupStartTagStart(position);
        if (tagStart < 0)
        {
            return null;
        }

        return root.DescendantNodes()
            .OfType<MarkupStartTagSyntax>()
            .LastOrDefault(candidate =>
                candidate.LessToken.Span.Start == tagStart &&
                !candidate.Name.IsMissing);
    }

    private int GetMarkupStartTagStart(int position)
    {
        if (Text.Length == 0 || position < 0)
        {
            return -1;
        }

        var start = Math.Min(position, Text.Length - 1);
        while (start >= 0 &&
               Text[start] is not ('<' or '>'))
        {
            start--;
        }

        if (start < 0 || Text[start] != '<' ||
            (start + 1 < Text.Length &&
             Text[start + 1] is '/' or '!' or '?'))
        {
            return -1;
        }

        var quote = '\0';
        for (var index = start + 1; index < position; index++)
        {
            var character = Text[index];
            if (quote == '\0')
            {
                if (character is '\'' or '"')
                {
                    quote = character;
                }
            }
            else if (character == quote &&
                     (index == start + 1 ||
                      Text[index - 1] != '\\'))
            {
                quote = '\0';
            }
        }

        return quote == '\0' ? start : -1;
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

    private bool TryGetDeclarationModifierContext(
        AkburaSyntax root,
        int position,
        out AkburaSyntacticCompletionContext context)
    {
        context = default;
        if (root is not AkburaDocumentSyntax document ||
            IsInsideComment(root, position))
        {
            return false;
        }

        var applicableSpan = GetTopLevelKeywordSpan(position);
        foreach (var declaration in document.Members
                     .OfType<ParamDeclarationSyntax>())
        {
            if (position < declaration.ParamKeyword.Span.End ||
                position > declaration.FullSpan.End ||
                declaration.Type is { IsMissing: false })
            {
                continue;
            }

            var bindingKeyword = declaration.BindingKeyword;
            if (bindingKeyword.RawKind != 0)
            {
                if (applicableSpan.Start !=
                        bindingKeyword.Span.Start ||
                    position > bindingKeyword.Span.End)
                {
                    continue;
                }
            }
            else if (applicableSpan.Start <
                         declaration.ParamKeyword.Span.End ||
                     !ContainsOnlyWhitespace(
                         Text,
                         declaration.ParamKeyword.Span.End,
                         applicableSpan.Start))
            {
                continue;
            }

            context = new AkburaSyntacticCompletionContext(
                AkburaCompletionContextKind.DeclarationModifier,
                applicableSpan,
                Text.ToString(applicableSpan),
                componentName: null,
                parentComponentName: null,
                ImmutableArray<string>.Empty);
            return true;
        }

        return false;
    }

    private bool TryGetTopLevelKeywordContext(
        AkburaSyntax root,
        int position,
        out AkburaSyntacticCompletionContext context)
    {
        context = default;
        if (root is not AkburaDocumentSyntax document ||
            IsInsideComment(root, position) ||
            TryGetEmbeddedCSharpContext(
                position,
                out var embeddedContext) &&
            embeddedContext.Kind is
                AkburaCSharpCompletionContextKind.UsingDirectiveName or
                AkburaCSharpCompletionContextKind.Type)
        {
            return false;
        }

        var applicableSpan = GetTopLevelKeywordSpan(position);
        AkTopLevelMemberSyntax? owner = null;
        foreach (var member in document.Members)
        {
            if (applicableSpan.Length > 0
                    ? member.Span.OverlapsWith(applicableSpan)
                    : member.Span.Start < position &&
                      position < member.Span.End)
            {
                owner = member;
                break;
            }
        }

        if (owner != null)
        {
            if (owner is not CSharpStatementSyntax statement ||
                statement.Span.Start > applicableSpan.Start ||
                !ContainsOnlyWhitespace(
                    Text,
                    statement.Span.Start,
                    applicableSpan.Start))
            {
                return false;
            }
        }

        context = new AkburaSyntacticCompletionContext(
            AkburaCompletionContextKind.TopLevel,
            applicableSpan,
            Text.ToString(applicableSpan),
            componentName: null,
            parentComponentName: null,
            ImmutableArray<string>.Empty);
        return true;
    }

    private TextSpan GetTopLevelKeywordSpan(int position)
    {
        var start = position;
        while (start > 0 &&
               (char.IsLetterOrDigit(Text[start - 1]) ||
                Text[start - 1] == '_'))
        {
            start--;
        }

        return TextSpan.FromBounds(start, position);
    }

    private static bool IsInsideComment(
        AkburaSyntax root,
        int position)
    {
        if (position == 0 || root.FullSpan.Length == 0)
        {
            return false;
        }

        var trivia = root.FindTrivia(
            Math.Min(position - 1, root.FullSpan.End - 1));
        return trivia.Kind is
            SyntaxKind.SingleLineCommentTrivia or
            SyntaxKind.MultiLineCommentTrivia;
    }

    private static bool ContainsOnlyWhitespace(
        SourceText text,
        int start,
        int end)
    {
        for (var position = start; position < end; position++)
        {
            if (!char.IsWhiteSpace(text[position]))
            {
                return false;
            }
        }

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
            position < closeToken.Span.End;
    }

    private static bool IsBeforeStartTagClose(
        int position,
        MarkupStartTagSyntax startTag)
    {
        // The start-tag span is the authoritative boundary after a list of
        // attributes, including incomplete attributes recovered as utilities.
        return startTag.CloseToken.IsMissing ||
            position < startTag.Span.End;
    }

    private bool IsInsideAttributeValue(
        MarkupStartTagSyntax startTag,
        int position)
    {
        foreach (var attribute in startTag.Attributes)
        {
            var inside = attribute switch
            {
                MarkupPlainAttributeSyntax plain =>
                    IsInsideAssignedAttributeValue(
                        plain.EqualsToken,
                        plain.Value,
                        plain.Span,
                        position),
                MarkupAttachedPropertyAttributeSyntax attached =>
                    IsInsideAssignedAttributeValue(
                        attached.EqualsToken,
                        attached.Value,
                        attached.Span,
                        position),
                MarkupPrefixedAttributeSyntax prefixed =>
                    IsInsideAssignedAttributeValue(
                        prefixed.EqualsToken,
                        prefixed.Value,
                        prefixed.Span,
                        position),
                IncompleteAttributeSyntax incomplete =>
                    !incomplete.EqualsToken.IsMissing &&
                    position >= incomplete.EqualsToken.Span.End &&
                    position <= incomplete.Span.End,
                _ => false,
            };
            if (inside)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInsideAssignedAttributeValue(
        SyntaxToken equalsToken,
        MarkupAttributeValueSyntax? value,
        TextSpan attributeSpan,
        int position)
    {
        if (equalsToken.IsMissing ||
            position < equalsToken.Span.End)
        {
            return false;
        }

        if (value != null &&
            (position < value.Span.End ||
             position == value.Span.End &&
             !IsCompleteAttributeValue(value)))
        {
            return true;
        }

        return position < attributeSpan.End;
    }

    private bool IsCompleteAttributeValue(
        MarkupAttributeValueSyntax value)
    {
        return value switch
        {
            MarkupLiteralAttributeValueSyntax literal =>
                IsCompleteQuotedAttributeValue(literal),
            MarkupDynamicAttributeValueSyntax dynamic =>
                !dynamic.Expression.CloseBrace.IsMissing,
            MarkupExtensionAttributeValueSyntax extension =>
                !extension.Extension.CloseBrace.IsMissing,
            _ => false,
        };
    }

    private bool IsCompleteQuotedAttributeValue(
        MarkupLiteralAttributeValueSyntax value)
    {
        if (value.Span.Length < 2)
        {
            return false;
        }

        var quote = Text[value.Span.Start];
        var end = value.Span.End - 1;
        while (end > value.Span.Start &&
               char.IsWhiteSpace(Text[end]))
        {
            end--;
        }

        return quote is '\'' or '"' &&
            Text[end] == quote;
    }

    private static ImmutableArray<string> GetExistingAttributeNames(
        MarkupStartTagSyntax startTag)
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
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
        var best = GetOpenElementStartTag(root, position);
        var name = best?.Name.ToFullString().Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static MarkupStartTagSyntax? GetOpenElementStartTag(
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

        return best;
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
