using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language;

internal sealed partial class Lexer
{
    private TokenInfo ParseMarkupForeachHeader()
    {
        var text = ScanMarkupCodeFragment(header: true);
        var statement = CSharpFactory.ParseStatement("foreach (" + text + ") {}", consumeFullText: true);
        return new TokenInfo
        {
            Kind = SyntaxKind.CSharpRawToken,
            ContextualKind = SyntaxKind.CSharpRawToken,
            Text = text,
            CSharpNode = statement,
            CSharpSyntaxKind = statement.Kind()
        };
    }

    private TokenInfo ParseMarkupCodeStatement()
    {
        var text = ScanMarkupCodeFragment(header: false);
        var statement = CSharpFactory.ParseStatement(text, consumeFullText: true);
        return new TokenInfo
        {
            Kind = SyntaxKind.CSharpRawToken,
            ContextualKind = SyntaxKind.CSharpRawToken,
            Text = text,
            CSharpNode = statement,
            CSharpSyntaxKind = statement.Kind()
        };
    }

    private string ScanMarkupCodeFragment(bool header)
    {
        var text = new StringBuilder();
        var parens = 0;
        var brackets = 0;
        var braces = 0;
        while (true)
        {
            var character = TextWindow.PeekChar();
            var atBoundary = parens == 0 && brackets == 0 && braces == 0;
            if (character == SlidingTextWindow.InvalidCharacter || character == '<' && TextWindow.PeekChar(1) == '/' ||
                atBoundary && (character == '}' || header && character is ')' or ';'))
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

            if (atBoundary && character == '{' && header && ForeachHeaderBraceStartsBody(text))
            {
                break;
            }

            if (atBoundary && header && character == '<' && MarkupCodeFragmentStartsSibling(character, text))
            {
                break;
            }

            if (atBoundary && !header && character is '<' or '$' && MarkupCodeFragmentStartsSibling(character, text))
            {
                break;
            }

            text.Append(character);
            TextWindow.AdvanceChar();
            switch (character)
            {
                case '(': parens++; break;
                case ')': if (parens > 0) parens--; break;
                case '[': brackets++; break;
                case ']': if (brackets > 0) brackets--; break;
                case '{': braces++; break;
                case '}': if (braces > 0) braces--; break;
            }

            if (!header && atBoundary && character == ';')
            {
                break;
            }
        }

        return text.ToString();
    }

    private bool ForeachHeaderBraceStartsBody(StringBuilder text)
    {
        var statement = CSharpFactory.ParseStatement("foreach (" + text + ") {}");
        if (statement is not CSharp.ForEachStatementSyntax loop || loop.Expression.IsMissing)
        {
            return true;
        }

        var offset = 1;
        while (char.IsWhiteSpace(TextWindow.PeekChar(offset))) offset++;
        var next = TextWindow.PeekChar(offset);
        if (next is '<' or '$' or '}') return true;
        return !statement.ContainsDiagnostics && loop.Expression is not
            (CSharp.ObjectCreationExpressionSyntax { Initializer: null } or
             CSharp.ImplicitObjectCreationExpressionSyntax { Initializer: null } or
             CSharp.ArrayCreationExpressionSyntax { Initializer: null } or
             CSharp.ImplicitArrayCreationExpressionSyntax { Initializer: null });
    }

    private bool MarkupCodeFragmentStartsSibling(char character, StringBuilder text) => character == '$'
        ? TextWindow.PeekChar(1) is 'i' or 'f' or 'e'
        : char.IsUpper(TextWindow.PeekChar(1)) && (text.Length == 0 || char.IsWhiteSpace(text[text.Length - 1]));
}
