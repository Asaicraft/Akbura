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
}
