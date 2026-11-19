using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

internal sealed partial class Lexer
{
    private TokenInfo ParseAkcssCSharpFragment(LexerMode mode)
    {
        var start = TextWindow.Position;
        var isParameterList = mode == LexerMode.InAkcssMixinParameters;
        var isList = isParameterList || mode == LexerMode.InAkcssIncludeArguments;
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        var hasParameterDefault = false;
        int? followingDirective = null;
        List<int>? parameterBraces = null;
        _builder.Clear();

        while (true)
        {
            var character = TextWindow.PeekChar();
            if (character == SlidingTextWindow.InvalidCharacter)
            {
                break;
            }

            var next = TextWindow.PeekChar(1);
            if (character == '/' && next is '/' or '*')
            {
                AppendMarkupConditionComment(_builder);
                _builder.Clear();
                continue;
            }

            if (TryScanCSharpStringOrCharText(out _))
            {
                continue;
            }

            // Even with an unbalanced parenthesis, leave the enclosing AKCSS
            // or markup delimiters for the caller's missing-token recovery.
            if (character == '<' && next == '/' ||
                braces == 0 && (character is ';' or '}' ||
                    character == ')' && parentheses == 0 && brackets == 0 ||
                    brackets == 0 && (character is '^' or '&') && IsFollowingAkcssSelector()))
            {
                break;
            }

            if (isParameterList && character == '{' && parentheses == 1 && brackets == 0 && braces == 0)
            {
                if (!hasParameterDefault)
                {
                    break;
                }

                (parameterBraces ??= []).Add(TextWindow.Position - start);
            }
            else if (character == '@' && braces == 0 && parentheses <= (isList ? 1 : 0) &&
                followingDirective is null && IsFollowingAkcssDirective())
            {
                followingDirective = TextWindow.Position - start;
            }

            switch (character)
            {
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
                case '}':
                    braces--;
                    break;
                case '=' when isParameterList && parentheses == 1 && brackets == 0 && braces == 0:
                    hasParameterDefault = true;
                    break;
            }

            TextWindow.AdvanceChar();
            if (isList && character == ')' && parentheses == 0 && brackets == 0 && braces == 0)
            {
                break;
            }
        }

        var text = TextWindow.Text.ToString(TextSpan.FromBounds(start, TextWindow.Position));
        var parsed = ParseAkcssRoslynFragment(text, mode);
        int? recoveryOffset = null;
        if ((parsed.ContainsDiagnostics || parsed.FullSpan.Length < text.Length) &&
            followingDirective is { } directive && directive < parsed.FullSpan.Length)
        {
            recoveryOffset = directive;
        }

        if (parsed is CSharp.ParameterListSyntax { ContainsDiagnostics: true } parameters && parameterBraces is not null)
        {
            // A brace after an earlier default may start the AKCSS body.
            // Roslyn's expression spans distinguish it from initializer and lambda braces.
            foreach (var brace in parameterBraces)
            {
                if (!parameters.Parameters.Any(parameter => parameter.Default?.Value.Span.Contains(brace) == true))
                {
                    recoveryOffset = recoveryOffset is { } previous ? Math.Min(previous, brace) : brace;
                    break;
                }
            }
        }

        if (recoveryOffset is { } offset)
        {
            text = text.Substring(0, offset);
            parsed = ParseAkcssRoslynFragment(text, mode);
        }

        if (parsed.FullSpan.Length < text.Length)
        {
            text = text.Substring(0, parsed.FullSpan.Length);
        }
        TextWindow.Reset(start + text.Length);

        return new TokenInfo
        {
            Kind = SyntaxKind.CSharpRawToken,
            ContextualKind = SyntaxKind.CSharpRawToken,
            Text = text,
            CSharpNode = parsed,
        };
    }

    private static SyntaxNode ParseAkcssRoslynFragment(string text, LexerMode mode) => mode switch
    {
        LexerMode.InAkcssMixinParameters => CSharpFactory.ParseParameterList(text),
        LexerMode.InAkcssIncludeArguments => CSharpFactory.ParseArgumentList(text),
        _ => CSharpFactory.ParseExpression(text, consumeFullText: mode != LexerMode.InAkcssBasedOnExpression),
    };

    private bool IsFollowingAkcssSelector()
    {
        // Only stop at punctuation that cannot start the right operand of a
        // C# binary expression. In particular, . followed by a digit is a literal.
        var offset = 1;
        while (char.IsWhiteSpace(TextWindow.PeekChar(offset)))
        {
            offset++;
        }

        var character = TextWindow.PeekChar(offset);
        var next = TextWindow.PeekChar(offset + 1);
        return character is ':' or '#' or '>' or '{' ||
            character == '.' && SyntaxFacts.IsIdentifierStartCharacter(next) ||
            character == '/' && next is not ('*' or '/');
    }

    private bool IsFollowingAkcssDirective()
    {
        // Record possible directive headers for recovery after Roslyn parsing.
        // These words may also be verbatim identifiers in valid C#.
        return IsAkcssDirectiveFollowedByName("theme") ||
            IsAkcssDirectiveFollowedByName("mixin") ||
            IsAkcssDirectiveFollowedByName("include");

        bool IsAkcssDirectiveFollowedByName(string name)
        {
            for (var index = 0; index < name.Length; index++)
            {
                if (TextWindow.PeekChar(index + 1) != name[index])
                {
                    return false;
                }
            }

            var offset = name.Length + 1;
            if (!char.IsWhiteSpace(TextWindow.PeekChar(offset)))
            {
                return false;
            }

            do
            {
                offset++;
            }
            while (char.IsWhiteSpace(TextWindow.PeekChar(offset)) &&
                !SyntaxFacts.IsNewLine(TextWindow.PeekChar(offset)));

            return SyntaxFacts.IsIdentifierStartCharacter(TextWindow.PeekChar(offset));
        }
    }
}
