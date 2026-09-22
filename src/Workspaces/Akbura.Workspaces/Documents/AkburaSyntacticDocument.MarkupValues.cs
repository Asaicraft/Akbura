using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Documents;

public sealed partial class AkburaSyntacticDocument
{
    private bool TryGetMarkupExtensionArgumentContext(AkburaSyntax root, int position, out AkburaSyntacticCompletionContext context)
    {
        context = default;
        if (!TryFindInnermostMarkupExtension(root, position, out var extension) ||
            position < extension.Type.Span.End)
        {
            return false;
        }

        if (TryGetEmbeddedCSharpContext(position, out _))
        {
            return false;
        }

        var extensionName = extension.Type
            .ToFullString()
            .Trim();
        if (extensionName.Length == 0)
        {
            return false;
        }

        var contentStart = extension.Type.Span.End;
        var contentEnd = GetMarkupExtensionContentEnd(extension);
        if (position < contentStart || position > contentEnd)
        {
            return false;
        }

        FindMarkupExtensionArgumentBounds(
            contentStart,
            contentEnd,
            position,
            out var argumentStart,
            out var argumentEnd,
            out var argumentIndex);
        var equals = FindTopLevelEquals(
            argumentStart,
            argumentEnd);
        var bindingName = GetUnqualifiedMarkupExtensionName(
            extensionName);
        var isBinding = bindingName is
            "Binding" or
            "ReflectionBinding" or
            "CompiledBinding";

        if ((equals < 0 && argumentIndex > 0) ||
            (equals >= 0 && position <= equals))
        {
            var nameEnd = equals < 0
                ? argumentEnd
                : equals;
            var nameSpan = GetMarkupWordSpan(
                position,
                argumentStart,
                nameEnd,
                includeBindingRootPrefix: false);
            context = CreateMarkupExtensionContext(
                AkburaCompletionContextKind
                    .MarkupExtensionArgumentName,
                nameSpan,
                position,
                extension,
                extensionName,
                argumentName: null,
                argumentIndex,
                completedPath: null);
            return true;
        }

        var argumentName = equals < 0
            ? null
            : Text.ToString(TextSpan.FromBounds(
                    argumentStart,
                    equals))
                .Trim();
        var valueStart = equals < 0
            ? argumentStart
            : equals + 1;
        GetMarkupValueContentBounds(
            valueStart,
            argumentEnd,
            out var valueContentStart,
            out var valueContentEnd);
        if (position < valueStart || position > argumentEnd)
        {
            return false;
        }

        var kind = isBinding &&
            (argumentIndex == 0 && equals < 0 ||
             string.Equals(
                 argumentName,
                 "Path",
                 StringComparison.Ordinal))
                ? AkburaCompletionContextKind.BindingPath
                : AkburaCompletionContextKind
                    .MarkupExtensionArgumentValue;
        var applicableSpan = position < valueContentStart || position > valueContentEnd
            ? new TextSpan(position, 0)
            : GetMarkupWordSpan(
                position,
                valueContentStart,
                valueContentEnd,
                includeBindingRootPrefix:
                    kind == AkburaCompletionContextKind.BindingPath);
        var completedPath = kind ==
                AkburaCompletionContextKind.BindingPath
            ? position <= valueContentStart
                ? string.Empty
                : Text.ToString(TextSpan.FromBounds(
                        valueContentStart,
                        Math.Min(applicableSpan.Start, valueContentEnd)))
                    .Trim()
            : null;

        context = CreateMarkupExtensionContext(
            kind,
            applicableSpan,
            position,
            extension,
            extensionName,
            argumentName,
            argumentIndex,
            completedPath);
        return true;
    }

    private bool TryFindInnermostMarkupExtension(AkburaSyntax root, int position, out MarkupExtensionSyntax extension)
    {
        MarkupExtensionSyntax? candidate = null;
        if (root.FullSpan.Length != 0)
        {
            var minimum = root.FullSpan.Start;
            var maximum = root.FullSpan.End - 1;
            ConsiderMarkupExtensionAncestors(root.FindToken(Math.Min(Math.Max(position, minimum), maximum)).Parent, position, ref candidate);
            ConsiderMarkupExtensionAncestors(root.FindToken(Math.Min(Math.Max(position - 1, minimum), maximum)).Parent, position, ref candidate);
            ConsiderMarkupExtensionAncestors(root.FindToken(Math.Min(Math.Max(position + 1, minimum), maximum)).Parent, position, ref candidate);
        }

        extension = candidate!;
        return candidate != null;
    }

    private void ConsiderMarkupExtensionAncestors(AkburaSyntax? node, int position, ref MarkupExtensionSyntax? candidate)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is not MarkupExtensionSyntax extension ||
                !HasMarkupExtensionDelimiters(extension) ||
                position < extension.OpenBrace.Span.End ||
                position > GetMarkupExtensionContentEnd(extension))
            {
                continue;
            }

            if (candidate == null || extension.OpenBrace.Span.Start > candidate.OpenBrace.Span.Start)
            {
                candidate = extension;
            }
        }
    }

    private bool TryFindMarkupStartTagOwner(AkburaSyntax root, int position, out MarkupStartTagSyntax startTag)
    {
        MarkupStartTagSyntax? candidate = null;
        if (root.FullSpan.Length != 0)
        {
            var minimum = root.FullSpan.Start;
            var maximum = root.FullSpan.End - 1;
            ConsiderMarkupStartTagAncestors(root.FindToken(Math.Min(Math.Max(position, minimum), maximum)).Parent, position, ref candidate);
            ConsiderMarkupStartTagAncestors(root.FindToken(Math.Min(Math.Max(position - 1, minimum), maximum)).Parent, position, ref candidate);
            ConsiderMarkupStartTagAncestors(root.FindToken(Math.Min(Math.Max(position + 1, minimum), maximum)).Parent, position, ref candidate);
        }

        startTag = candidate!;
        return candidate != null;
    }

    private void ConsiderMarkupStartTagAncestors(AkburaSyntax? node, int position, ref MarkupStartTagSyntax? candidate)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is not MarkupStartTagSyntax startTag ||
                !IsPositionOwnedByMarkupStartTag(startTag, position))
            {
                continue;
            }

            if (candidate == null || startTag.LessToken.Span.Start > candidate.LessToken.Span.Start)
            {
                candidate = startTag;
            }
        }
    }

    private bool IsPositionOwnedByMarkupStartTag(MarkupStartTagSyntax startTag, int position)
    {
        if (startTag.LessToken.IsMissing ||
            startTag.LessToken.Span.Start < 0 ||
            startTag.LessToken.Span.Start >= Text.Length ||
            Text[startTag.LessToken.Span.Start] != '<' ||
            position < startTag.LessToken.Span.End)
        {
            return false;
        }

        if (startTag.CloseToken.IsMissing)
        {
            return position <= Math.Min(startTag.FullSpan.End, Text.Length);
        }

        var closeEnd = Math.Min(startTag.Span.End, Text.Length);
        return closeEnd > startTag.LessToken.Span.End &&
            Text[closeEnd - 1] == '>' &&
            position < closeEnd;
    }

    private bool HasMarkupExtensionDelimiters(MarkupExtensionSyntax extension)
    {
        return !extension.DollarToken.IsMissing &&
            !extension.OpenBrace.IsMissing &&
            extension.DollarToken.Span.Start >= 0 &&
            extension.DollarToken.Span.Start < Text.Length &&
            extension.OpenBrace.Span.Start >= 0 &&
            extension.OpenBrace.Span.Start < Text.Length &&
            Text[extension.DollarToken.Span.Start] == '$' &&
            Text[extension.OpenBrace.Span.Start] == '{';
    }

    private bool IsPositionOwnedByMarkupAttributeValue(AkburaSyntax root, int position)
    {
        if (TryFindInnermostMarkupExtension(root, position, out _))
        {
            return true;
        }

        if (Text.Length == 0)
        {
            return false;
        }

        var tokenPosition = Math.Min(Math.Max(position - 1, 0), Text.Length - 1);
        var token = root.FindTokenInternal(tokenPosition);
        var startTag = GetStartTagAtPosition(root, token.Parent, position);
        if (startTag == null)
        {
            return false;
        }

        foreach (var attribute in startTag.Attributes)
        {
            if (IsPositionOwnedByMarkupAttribute(attribute, position))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPositionOwnedByMarkupAttribute(AkburaSyntax attribute, int position)
    {
        SyntaxToken equalsToken;
        MarkupAttributeValueSyntax? value;
        switch (attribute)
        {
            case MarkupPlainAttributeSyntax plain:
                equalsToken = plain.EqualsToken;
                value = plain.Value;
                break;
            case MarkupAttachedPropertyAttributeSyntax attached:
                equalsToken = attached.EqualsToken;
                value = attached.Value;
                break;
            case MarkupPrefixedAttributeSyntax prefixed:
                equalsToken = prefixed.EqualsToken;
                value = prefixed.Value;
                break;
            case IncompleteAttributeSyntax incomplete:
                equalsToken = incomplete.EqualsToken;
                value = null;
                break;
            default:
                return false;
        }

        if (equalsToken.IsMissing || position < equalsToken.Span.End)
        {
            return false;
        }

        if (value == null)
        {
            return position <= Math.Min(attribute.FullSpan.End, Text.Length);
        }

        var isComplete = IsCompleteAttributeValue(value);
        var valueEnd = isComplete
            ? value.Span.End
            : Math.Min(value.FullSpan.End, Text.Length);
        return isComplete ? position < valueEnd : position <= valueEnd;
    }

    private AkburaSyntacticCompletionContext CreateMarkupExtensionContext(AkburaCompletionContextKind kind, TextSpan applicableSpan, int position, MarkupExtensionSyntax extension, string extensionName, string? argumentName, int argumentIndex, string? completedPath)
    {
        var prefixEnd = Math.Min(
            Math.Max(position, applicableSpan.Start),
            applicableSpan.End);
        var prefixSpan = TextSpan.FromBounds(
            applicableSpan.Start,
            prefixEnd);
        return new AkburaSyntacticCompletionContext(
            kind,
            applicableSpan,
            Text.ToString(prefixSpan),
            componentName: null,
            parentComponentName: null,
            ImmutableArray<string>.Empty,
            attributeName: null,
            markupExtensionName: extensionName,
            markupExtensionArgumentName: argumentName,
            markupExtensionArgumentIndex: argumentIndex,
            completedPath: completedPath,
            markupExtensionSpan: extension.Span);
    }

    private int GetMarkupExtensionContentEnd(MarkupExtensionSyntax extension)
    {
        return extension.CloseBrace.IsMissing
            ? Math.Min(extension.FullSpan.End, Text.Length)
            : extension.CloseBrace.Span.Start;
    }

    private void FindMarkupExtensionArgumentBounds(int contentStart, int contentEnd, int position, out int argumentStart, out int argumentEnd, out int argumentIndex)
    {
        argumentStart = contentStart;
        argumentEnd = contentEnd;
        argumentIndex = 0;
        var quote = '\0';
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;

        for (var index = contentStart; index < contentEnd; index++)
        {
            var character = Text[index];
            if (quote != '\0')
            {
                if (character == quote &&
                    !IsEscaped(index, contentStart))
                {
                    quote = '\0';
                }

                continue;
            }

            switch (character)
            {
                case '\'' or '"':
                    quote = character;
                    break;
                case '(':
                    parentheses++;
                    break;
                case ')' when parentheses > 0:
                    parentheses--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']' when brackets > 0:
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}' when braces > 0:
                    braces--;
                    break;
                case ',' when parentheses == 0 &&
                    brackets == 0 && braces == 0:
                    if (index < position)
                    {
                        argumentStart = index + 1;
                        argumentIndex++;
                    }
                    else
                    {
                        argumentEnd = index;
                        return;
                    }

                    break;
            }
        }
    }

    private int FindTopLevelEquals(int start, int end)
    {
        var quote = '\0';
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;

        for (var index = start; index < end; index++)
        {
            var character = Text[index];
            if (quote != '\0')
            {
                if (character == quote &&
                    !IsEscaped(index, start))
                {
                    quote = '\0';
                }

                continue;
            }

            switch (character)
            {
                case '\'' or '"':
                    quote = character;
                    break;
                case '(':
                    parentheses++;
                    break;
                case ')' when parentheses > 0:
                    parentheses--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']' when brackets > 0:
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}' when braces > 0:
                    braces--;
                    break;
                case '=' when parentheses == 0 &&
                    brackets == 0 && braces == 0:
                    return index;
            }
        }

        return -1;
    }

    private void GetMarkupValueContentBounds(int start, int end, out int contentStart, out int contentEnd)
    {
        contentStart = start;
        while (contentStart < end &&
               char.IsWhiteSpace(Text[contentStart]))
        {
            contentStart++;
        }

        contentEnd = end;
        while (contentEnd > contentStart &&
               char.IsWhiteSpace(Text[contentEnd - 1]))
        {
            contentEnd--;
        }

        if (contentStart >= contentEnd ||
            Text[contentStart] is not ('\'' or '"'))
        {
            return;
        }

        var quote = Text[contentStart];
        contentStart++;
        if (contentEnd > contentStart &&
            Text[contentEnd - 1] == quote &&
            !IsEscaped(contentEnd - 1, contentStart))
        {
            contentEnd--;
        }
    }

    private bool IsEscaped(int position, int minimum)
    {
        var slashCount = 0;
        for (var index = position - 1; index >= minimum && Text[index] == '\\'; index--)
        {
            slashCount++;
        }

        return (slashCount & 1) != 0;
    }

    private TextSpan GetMarkupWordSpan(int position, int minimumStart, int maximumEnd, bool includeBindingRootPrefix)
    {
        var start = position;
        while (start > minimumStart &&
               IsMarkupValueWordCharacter(
                   Text[start - 1],
                   includeBindingRootPrefix))
        {
            start--;
        }

        var end = position;
        while (end < maximumEnd &&
               IsMarkupValueWordCharacter(
                   Text[end],
                   includeBindingRootPrefix))
        {
            end++;
        }

        return TextSpan.FromBounds(start, end);
    }

    private static bool IsMarkupValueWordCharacter(char character, bool includeBindingRootPrefix)
    {
        return char.IsLetterOrDigit(character) ||
            character is '_' or '-' ||
            includeBindingRootPrefix &&
            character is '$' or '#';
    }

    private static string GetUnqualifiedMarkupExtensionName(string name)
    {
        var normalized = name.Trim();
        var genericStart = normalized.IndexOf('<');
        if (genericStart >= 0)
        {
            normalized = normalized[..genericStart];
        }

        var aliasSeparator = normalized.LastIndexOf(
            "::",
            StringComparison.Ordinal);
        if (aliasSeparator >= 0)
        {
            normalized = normalized[(aliasSeparator + 2)..];
        }

        var namespaceSeparator = normalized.LastIndexOf('.');
        if (namespaceSeparator >= 0)
        {
            normalized = normalized[(namespaceSeparator + 1)..];
        }

        return normalized.EndsWith(
            "Extension",
            StringComparison.Ordinal)
                ? normalized[..^"Extension".Length]
                : normalized;
    }
}
