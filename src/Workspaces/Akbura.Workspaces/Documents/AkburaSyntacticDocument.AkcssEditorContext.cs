using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Documents;

public sealed partial class AkburaSyntacticDocument
{
    internal bool ContainsAkcss =>
        SyntaxTree is AkcssSyntaxTree ||
        SyntaxTree is ComponentSyntaxTree component && component.GetRoot().Members.Any(static member => member is InlineAkcssBlockSyntax);

    internal AkcssTemplateDirectiveSyntax? GetAkcssMarkupTemplate(int position)
    {
        if (!TryGetAkcssRegion(position, out var region))
        {
            return null;
        }

        AkcssTemplateDirectiveSyntax? result = null;
        foreach (var node in region.Root!.DescendantNodesAtPosition(position))
        {
            if (node is AkcssTemplateDirectiveSyntax template && IsInTemplateMarkup(template, position))
            {
                result = template;
            }
        }

        return result;
    }

    internal bool IsAkcssFormattingSensitive(int lineStart, int firstContent, CancellationToken cancellationToken)
    {
        if (!TryGetAkcssRegion(firstContent, out var region))
        {
            return false;
        }

        var root = region.Root!;
        if (IsInsideComment(root, lineStart) ||
            TryGetEmbeddedCSharpContext(lineStart, out var fragment, cancellationToken) && fragment.HostSpan.Start < lineStart)
        {
            return true;
        }

        var inTemplate = false;
        foreach (var node in root.DescendantNodesAtPosition(firstContent))
        {
            if (node is AkcssTemplateDirectiveSyntax template && IsInTemplateMarkup(template, firstContent))
            {
                inTemplate = true;
            }
            else if (inTemplate && node is MarkupTextLiteralSyntax && node.Span.Contains(firstContent))
            {
                return true;
            }
        }

        return false;
    }

    internal AkcssIncludeSyntax? GetAkcssIncludeAtPosition(int position)
    {
        if (!TryGetAkcssRegion(position, out var region))
        {
            return null;
        }

        AkcssIncludeSyntax? result = null;
        foreach (var node in region.Root!.DescendantNodesAtPosition(position))
        {
            if (node is AkcssIncludeSyntax include)
            {
                result = include;
            }
        }

        return result;
    }

    private static bool IsInTemplateMarkup(AkcssTemplateDirectiveSyntax template, int position) =>
        !template.Body.OpenBraceToken.IsMissing &&
        position >= template.Body.OpenBraceToken.Span.End &&
        (template.Body.CloseBraceToken.IsMissing || position <= template.Body.CloseBraceToken.Span.Start);

    private AkcssSyntacticCompletionContext GetContextualAkcssCompletion(
        AkburaSyntax root, AkburaSyntax? declaration, int position)
    {
        AkcssIncludeSyntax? include = null;
        AkcssTemplateBindSyntax? binding = null;
        AkcssBasedOnDirectiveSyntax? basedOn = null;
        AkcssSelectorSyntax? selector = null;
        AkcssNameSelectorSyntax? name = null;
        AkcssPseudoClassSelectorSyntax? pseudo = null;
        MarkupElementSyntax? element = null;
        var inTemplate = false;
        foreach (var node in root.DescendantNodesAtPosition(position))
        {
            switch (node)
            {
                case AkcssIncludeSyntax value:
                    include = value;
                    break;
                case AkcssTemplateBindSyntax value:
                    binding = value;
                    break;
                case AkcssBasedOnDirectiveSyntax value:
                    basedOn = value;
                    break;
                case AkcssSelectorSyntax value:
                    selector = value;
                    break;
                case AkcssNameSelectorSyntax value:
                    name = value;
                    break;
                case AkcssPseudoClassSelectorSyntax value:
                    pseudo = value;
                    break;
                case MarkupElementSyntax value:
                    element = value;
                    break;
                case AkcssTemplateDirectiveSyntax template when IsInTemplateMarkup(template, position):
                    inTemplate = true;
                    break;
            }
        }

        if (include is not null && position >= include.IncludeKeyword.Span.End)
        {
            if (include.Arguments.OpenParen.IsMissing || position <= include.Arguments.OpenParen.Span.Start)
            {
                return CreateContext(AkcssCompletionContextKind.IncludeName, GetFragment(position, IsIdentifier), include);
            }

            if (include.Arguments.CloseParen.IsMissing || position <= include.Arguments.CloseParen.Span.Start)
            {
                return CreateContext(AkcssCompletionContextKind.IncludeArgument, new TextSpan(position, 0), include.Arguments);
            }
        }

        if (binding is not null &&
            position >= binding.BindToken.Span.End &&
            (binding.Semicolon.IsMissing || position <= binding.Semicolon.Span.Start))
        {
            return CreateContext(AkcssCompletionContextKind.TemplateBindingProperty,
                GetFragment(position, IsIdentifier), binding);
        }

        if (basedOn is not null &&
            position >= basedOn.OnKeyword.Span.End &&
            (basedOn.Semicolon.IsMissing || position <= basedOn.Semicolon.Span.Start))
        {
            var span = GetFragment(position, IsIdentifier);
            var qualifier = span.Start > basedOn.Expression.Span.Start && Text[span.Start - 1] == '.'
                ? Text.ToString(TextSpan.FromBounds(basedOn.Expression.Span.Start, span.Start - 1)).Trim()
                : string.Empty;
            return CreateContext(AkcssCompletionContextKind.BaseTheme, span, basedOn, qualifier);
        }

        if (selector is not null)
        {
            if (name is not null && position >= name.HashToken.Span.End)
            {
                return CreateContext(AkcssCompletionContextKind.TemplatePart, GetFragment(position, IsSelectorName), name);
            }

            if (pseudo is not null && position >= pseudo.ColonToken.Span.End)
            {
                return CreateContext(AkcssCompletionContextKind.PseudoClass, GetFragment(position, IsSelectorName), pseudo);
            }

            var fragment = GetFragment(position, static value => IsIdentifier(value) || value == '/');
            if (fragment.Length != 0 && Text[fragment.Start] == '/' &&
                "/template/".StartsWith(Text.ToString(TextSpan.FromBounds(fragment.Start, position)), StringComparison.Ordinal))
            {
                return CreateContext(AkcssCompletionContextKind.TemplateTraversal, fragment, selector);
            }

            return CreateContext(AkcssCompletionContextKind.SelectorSnippet, GetAkcssSelectorSnippetSpan(position), selector);
        }

        if (inTemplate)
        {
            var fragment = GetFragment(position, static value => IsSelectorName(value) || value == '@');
            var prefix = Text.ToString(TextSpan.FromBounds(fragment.Start, position));
            if (prefix.StartsWith("@", StringComparison.Ordinal) &&
                !TryGetEmbeddedCSharpContext(position, out _) &&
                element is { StartTag: { } tag } &&
                !tag.CloseToken.IsMissing && position >= tag.CloseToken.Span.End &&
                (element.EndTag is null || element.EndTag.IsMissing || position <= element.EndTag.Span.Start))
            {
                return CreateContext(AkcssCompletionContextKind.TemplateMarkupBody, fragment, element);
            }
        }

        return default;

        AkcssSyntacticCompletionContext CreateContext(AkcssCompletionContextKind kind, TextSpan span, AkburaSyntax site, string? qualifier = null) =>
            new(kind, span, Text.ToString(TextSpan.FromBounds(span.Start, position)), site.Kind, site.FullSpan,
                declaration?.FullSpan ?? default, qualifier, position: position);
    }

    private TextSpan GetFragment(int position, Func<char, bool> predicate)
    {
        var start = position;
        var end = position;
        while (start > 0 && predicate(Text[start - 1]))
        {
            start--;
        }

        while (end < Text.Length && predicate(Text[end]))
        {
            end++;
        }

        return TextSpan.FromBounds(start, end);
    }

    private static bool IsIdentifier(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static bool IsSelectorName(char value) => IsIdentifier(value) || value == '-';
}
