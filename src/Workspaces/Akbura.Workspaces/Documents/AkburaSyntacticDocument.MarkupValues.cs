using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Documents;

public sealed partial class AkburaSyntacticDocument
{
    private bool TryGetMarkupExtensionArgumentContext(AkburaSyntax root, int position, out AkburaSyntacticCompletionContext context)
    {
        context = default;

        MarkupExtensionSyntax? extension = null;
        if (root.FullSpan.Length != 0)
        {
            var tokenPosition = Math.Min(
                Math.Max(position, root.FullSpan.Start),
                root.FullSpan.End - 1);
            for (var node = root.FindToken(tokenPosition).Parent; node != null; node = node.Parent)
            {
                if (node is MarkupExtensionSyntax candidate &&
                    candidate.OpenBrace.Span.End <= position &&
                    position <= GetMarkupExtensionContentEnd(candidate))
                {
                    extension = candidate;
                    break;
                }
            }
        }
        if (extension == null ||
            position < extension.Type.FullSpan.End)
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

        var contentStart = extension.Type.FullSpan.End;
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
        if (position < valueContentStart ||
            position > valueContentEnd)
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
        var applicableSpan = GetMarkupWordSpan(
            position,
            valueContentStart,
            valueContentEnd,
            includeBindingRootPrefix:
                kind == AkburaCompletionContextKind.BindingPath);
        var completedPath = kind ==
                AkburaCompletionContextKind.BindingPath
            ? Text.ToString(TextSpan.FromBounds(
                    valueContentStart,
                    applicableSpan.Start))
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
