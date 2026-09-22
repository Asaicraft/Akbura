using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Completion;

internal readonly struct AkburaResourceKeyCompletionContext
{
    public AkburaResourceKeyCompletionContext(string extensionName, MarkupExtensionSyntax extension, TextSpan applicableSpan, string prefix, char quote)
    {
        ExtensionName = extensionName;
        Extension = extension ??
            throw new ArgumentNullException(nameof(extension));
        ApplicableSpan = applicableSpan;
        Prefix = prefix;
        Quote = quote;
    }

    public string ExtensionName { get; }

    public MarkupExtensionSyntax Extension { get; }

    public TextSpan ApplicableSpan { get; }

    public string Prefix { get; }

    public char Quote { get; }
}

internal static class AkburaResourceKeyCompletionFacts
{
    public static bool TryGetContext(AkburaSyntacticDocument document, int position, out AkburaResourceKeyCompletionContext context, CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }
        context = default;

        if ((uint)position > (uint)document.Text.Length ||
            document.SyntaxTree.Kind == SyntaxTreeKind.Akcss)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var extension = FindInnermostResourceExtension(
            document.SyntaxTree.GetRootSyntax(),
            position,
            cancellationToken);
        if (extension == null)
        {
            return false;
        }

        var extensionName = extension.Type
            .ToFullString()
            .Trim();
        if (!TryGetResourceArgument(
                extension,
                position,
                out var value,
                out var valueStart))
        {
            return false;
        }

        if (!TryGetLiteralContentSpan(
                document.Text,
                extension,
                value,
                valueStart,
                position,
                out var applicableSpan,
                out var quote))
        {
            return false;
        }

        var prefixEnd = Math.Min(position, applicableSpan.End);
        var prefix = prefixEnd <= applicableSpan.Start
            ? string.Empty
            : document.Text.ToString(
                TextSpan.FromBounds(
                    applicableSpan.Start,
                    prefixEnd));
        context = new AkburaResourceKeyCompletionContext(
            extensionName,
            extension,
            applicableSpan,
            prefix,
            quote);
        return true;
    }

    private static MarkupExtensionSyntax? FindInnermostResourceExtension(AkburaSyntax root, int position, CancellationToken cancellationToken)
    {
        if (root.FullSpan.Length == 0)
        {
            return null;
        }

        var tokenPosition = Math.Min(
            Math.Max(position, root.FullSpan.Start),
            root.FullSpan.End - 1);
        for (var node = root.FindToken(tokenPosition).Parent; node != null; node = node.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is not MarkupExtensionSyntax extension)
            {
                continue;
            }

            var start = extension.OpenBrace.Span.End;
            var end = extension.CloseBrace.IsMissing
                ? extension.FullSpan.End
                : extension.CloseBrace.Span.Start;
            if (position < start || position > end)
            {
                continue;
            }

            var name = extension.Type
                .ToFullString()
                .Trim();
            if (!IsResourceExtensionName(name))
            {
                continue;
            }

            return extension;
        }

        return null;
    }

    private static bool IsResourceExtensionName(string name)
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

        if (normalized.EndsWith(
                "Extension",
                StringComparison.Ordinal))
        {
            normalized = normalized[..^"Extension".Length];
        }

        return normalized is "StaticResource" or "DynamicResource";
    }

    private static bool TryGetResourceArgument(MarkupExtensionSyntax extension, int position, out MarkupExtensionValueSyntax? value, out int valueStart)
    {
        value = null;
        valueStart = position;

        for (var index = 0; index < extension.Arguments.Count; index++)
        {
            var argument = extension.Arguments[index];
            if (position < argument.FullSpan.Start ||
                position > argument.FullSpan.End)
            {
                continue;
            }

            switch (argument)
            {
                case MarkupExtensionPositionalArgumentSyntax positional
                    when index == 0:
                    value = positional.Value;
                    valueStart = positional.Value.FullSpan.Start;
                    return true;

                case MarkupExtensionPropertyArgumentSyntax property
                    when string.Equals(
                        property.Name.ToFullString().Trim(),
                        "ResourceKey",
                        StringComparison.Ordinal):
                    if (position < property.EqualsToken.Span.End)
                    {
                        return false;
                    }

                    value = property.Value;
                    valueStart = property.Value.FullSpan.Start;
                    return true;

                default:
                    return false;
            }
        }

        if (extension.Arguments.Count != 0)
        {
            var last = extension.Arguments[^1];
            if (position < last.FullSpan.End)
            {
                return false;
            }

            return false;
        }

        if (position < extension.Type.FullSpan.End)
        {
            return false;
        }

        valueStart = position;
        return true;
    }

    private static bool TryGetLiteralContentSpan(SourceText text, MarkupExtensionSyntax extension, MarkupExtensionValueSyntax? value, int valueStart, int position, out TextSpan contentSpan, out char quote)
    {
        contentSpan = default;
        quote = '\0';
        if (value is MarkupExtensionExpressionValueSyntax or
            MarkupExtensionNestedValueSyntax)
        {
            return false;
        }

        var boundary = extension.CloseBrace.IsMissing
            ? extension.FullSpan.End
            : extension.CloseBrace.Span.Start;
        var start = Math.Max(
            extension.Type.FullSpan.End,
            Math.Min(valueStart, boundary));
        var end = value == null
            ? position
            : Math.Min(value.FullSpan.End, boundary);

        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        if (start < end && text[start] is '\'' or '"')
        {
            quote = text[start];
            start++;
            if (end > start && text[end - 1] == quote &&
                !IsEscaped(text, end - 1, start))
            {
                end--;
            }
        }

        if (position < start || position > end)
        {
            return false;
        }

        contentSpan = TextSpan.FromBounds(start, end);
        return true;
    }

    private static bool IsEscaped(SourceText text, int position, int minimum)
    {
        var slashCount = 0;
        for (var index = position - 1; index >= minimum && text[index] == '\\'; index--)
        {
            slashCount++;
        }

        return (slashCount & 1) != 0;
    }
}
