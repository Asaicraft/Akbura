using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language;

internal sealed partial class Lexer
{
    private TokenInfo ParseMarkupCondition()
    {
        var text = new StringBuilder();
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        while (true)
        {
            var character = TextWindow.PeekChar();
            if (character == SlidingTextWindow.InvalidCharacter ||
                character == '<' && TextWindow.PeekChar(1) == '/' ||
                character == ')' && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 ||
                character == '}' && braceDepth == 0)
            {
                break;
            }

            if (character == '/' && TextWindow.PeekChar(1) is '/' or '*')
            {
                AppendMarkupConditionComment(text);
                continue;
            }

            if (TryScanCSharpStringOrCharText(out var literal))
            {
                text.Append(literal);
                continue;
            }

            if (character == '{' && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 &&
                IsMarkupConditionBlockBoundary(text))
            {
                break;
            }

            switch (character)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0) parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0) bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    break;
            }

            text.Append(character);
            TextWindow.AdvanceChar();
        }

        var rawText = text.ToString();
        var expression = CSharpFactory.ParseExpression(rawText, consumeFullText: true);
        return new TokenInfo
        {
            Kind = SyntaxKind.CSharpRawToken,
            ContextualKind = SyntaxKind.CSharpRawToken,
            Text = rawText,
            CSharpNode = expression,
            CSharpSyntaxKind = expression.Kind()
        };
    }

    private bool IsMarkupConditionBlockBoundary(StringBuilder text)
    {
        var rawText = text.ToString();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return true;
        }

        var prefix = CSharpFactory.ParseExpression(rawText, consumeFullText: true);
        if (prefix.ContainsDiagnostics)
        {
            return false;
        }

        if (MarkupConditionBraceStartsMarkup())
        {
            return true;
        }

        return prefix switch
        {
            CSharp.ObjectCreationExpressionSyntax creation => creation.Initializer != null,
            CSharp.ImplicitObjectCreationExpressionSyntax creation => creation.Initializer != null,
            CSharp.IsPatternExpressionSyntax pattern => !CanOpenMarkupConditionPropertyPattern(pattern.Pattern),
            CSharp.BinaryExpressionSyntax binary when binary.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.IsExpression => false,
            _ => true,
        };
    }

    private static bool CanOpenMarkupConditionPropertyPattern(CSharp.PatternSyntax pattern) => pattern switch
    {
        CSharp.TypePatternSyntax => true,
        CSharp.ConstantPatternSyntax { Expression: CSharp.NameSyntax or CSharp.MemberAccessExpressionSyntax } => true,
        CSharp.RecursivePatternSyntax recursive => recursive.PropertyPatternClause == null,
        CSharp.UnaryPatternSyntax unary => CanOpenMarkupConditionPropertyPattern(unary.Pattern),
        CSharp.BinaryPatternSyntax binary => CanOpenMarkupConditionPropertyPattern(binary.Right),
        _ => false,
    };

    private bool MarkupConditionBraceStartsMarkup()
    {
        var offset = 1;
        while (char.IsWhiteSpace(TextWindow.PeekChar(offset)))
        {
            offset++;
        }

        var character = TextWindow.PeekChar(offset);
        return character == '<' || character == '$' &&
            (TextWindow.PeekChar(offset + 1) is 'i' or 'e');
    }

    private void AppendMarkupConditionComment(StringBuilder text)
    {
        var lineComment = TextWindow.PeekChar(1) == '/';
        text.Append(TextWindow.NextChar());
        text.Append(TextWindow.NextChar());
        while (TextWindow.PeekChar() != SlidingTextWindow.InvalidCharacter)
        {
            var character = TextWindow.NextChar();
            text.Append(character);
            if (lineComment && Akbura.Language.Syntax.SyntaxFacts.IsNewLine(character))
            {
                break;
            }
            if (!lineComment && character == '*' && TextWindow.PeekChar() == '/')
            {
                text.Append(TextWindow.NextChar());
                break;
            }
        }
    }
}
