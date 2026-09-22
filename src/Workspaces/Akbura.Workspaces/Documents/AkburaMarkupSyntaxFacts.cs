using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Documents;

internal static class AkburaMarkupSyntaxFacts
{
    internal static TextSpan GetAttributeLiteralSpan(MarkupLiteralAttributeValueSyntax literal)
    {
        var span = literal.Span;
        var text = literal.ToString();
        if (text.Length < 2 || text[0] is not ('\'' or '"'))
        {
            return span;
        }

        var end = text.Length;
        while (end > 1 && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        // The parser folds trailing trivia from the closing quote into the literal token.
        // Keep whitespace inside an incomplete literal, but exclude trivia after a real quote.
        return text[end - 1] == text[0]
            ? new TextSpan(span.Start, end)
            : span;
    }

    internal static bool TryGetAttributeLiteralContentSpan(SourceText text, MarkupLiteralAttributeValueSyntax literal, out TextSpan contentSpan)
    {
        var literalSpan = GetAttributeLiteralSpan(literal);
        if (literalSpan.Length == 0 ||
            literalSpan.Start >= text.Length ||
            text[literalSpan.Start] is not ('\'' or '"'))
        {
            contentSpan = default;
            return false;
        }

        var quote = text[literalSpan.Start];
        var start = literalSpan.Start + 1;
        var end = literalSpan.End;
        if (end > start && text[end - 1] == quote)
        {
            end--;
        }
        else
        {
            var angleDepth = 0;
            for (var index = start; index < end; index++)
            {
                if (text[index] == '<')
                {
                    angleDepth++;
                    continue;
                }

                if (text[index] != '>')
                {
                    continue;
                }

                if (angleDepth > 0)
                {
                    angleDepth--;
                    continue;
                }

                if (index > start && text[index - 1] == '/')
                {
                    end = index - 1;
                }
                else
                {
                    end = index;
                }

                break;
            }
        }

        contentSpan = TextSpan.FromBounds(start, end);
        return true;
    }
}
