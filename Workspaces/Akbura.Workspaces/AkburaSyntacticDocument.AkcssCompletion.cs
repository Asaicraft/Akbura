using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Workspaces;

public sealed partial class AkburaSyntacticDocument
{
    internal bool TryGetAkcssCompletionRegion(
        int position,
        out AkcssCompletionRegion region)
    {
        ValidatePosition(position);
        return AkcssCompletionRegion.TryCreate(
            SyntaxTree,
            Text,
            position,
            out region);
    }

    /// <summary>
    /// Determines the AKCSS completion construct at
    /// <paramref name="position"/>.
    /// </summary>
    public AkcssSyntacticCompletionContext GetAkcssCompletionContext(
        int position,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetAkcssCompletionRegion(
                position,
                out var region))
        {
            return default;
        }

        var root = region.Root!;
        if (IsInsideComment(root, position))
        {
            return default;
        }

        var declaration = FindContainingAkcssDeclaration(
            root,
            position);
        var owner = FindContainingAkcssBodyOwner(
            declaration,
            position) ?? declaration;

        if (TryGetAkcssAttachedPropertyExpressionContext(
                declaration,
                owner,
                position,
                out var attachedPropertyContext))
        {
            return attachedPropertyContext;
        }

        var apply = FindContainingAkcssApply(
            declaration,
            position);
        if (apply != null &&
            AkcssApplyItemFacts.TryGetCompletionItem(
                Text,
                apply,
                position,
                out var item))
        {
            var clampedSpan = ClampToPosition(
                item.Span,
                position);
            return CreateAkcssContext(
                AkcssCompletionContextKind.ApplyItem,
                clampedSpan,
                owner,
                declaration);
        }

        var assignment = FindContainingAkcssAssignment(
            declaration,
            position);
        if (assignment != null)
        {
            if (!assignment.Colon.IsMissing &&
                position >= assignment.Colon.Span.End &&
                IsBeforeAkcssMemberEnd(
                    assignment.Semicolon,
                    assignment,
                    position))
            {
                var valueSpan = GetAkcssValueSpan(position);
                return CreateAkcssContext(
                    AkcssCompletionContextKind.PropertyValue,
                    valueSpan,
                    owner,
                    declaration,
                    propertyName: assignment.PropertyName
                        .ToFullString()
                        .Trim());
            }

            var assignmentLine = Text.Lines.GetLineFromPosition(
                position);
            var recoveredColon = FindAkcssAssignmentColon(
                assignmentLine.Start,
                position);
            if (recoveredColon >= 0)
            {
                return CreateAkcssContext(
                    AkcssCompletionContextKind.PropertyValue,
                    GetAkcssValueSpan(position),
                    owner,
                    declaration,
                    propertyName: GetTextualAkcssPropertyName(
                        assignmentLine.Start,
                        recoveredColon));
            }

            return CreateAkcssPropertyNameContext(
                position,
                owner,
                declaration);
        }

        var line = Text.Lines.GetLineFromPosition(position);
        var linePrefix = Text.ToString(
            TextSpan.FromBounds(line.Start, position));
        var trimmedLinePrefix = linePrefix.TrimStart();

        if (declaration != null)
        {
            if (trimmedLinePrefix.StartsWith(
                    "@apply",
                    StringComparison.Ordinal))
            {
                var textualItemSpan = GetTextualAkcssApplyItemSpan(
                    line.Start,
                    position);
                return CreateAkcssContext(
                    AkcssCompletionContextKind.ApplyItem,
                    textualItemSpan,
                    owner,
                    declaration);
            }

            var colon = FindAkcssAssignmentColon(
                line.Start,
                position);
            if (colon >= 0)
            {
                var valueSpan = GetAkcssValueSpan(position);
                return CreateAkcssContext(
                    AkcssCompletionContextKind.PropertyValue,
                    valueSpan,
                    owner,
                    declaration,
                    propertyName: GetTextualAkcssPropertyName(
                        line.Start,
                        colon));
            }

            if (trimmedLinePrefix.Length == 0 ||
                trimmedLinePrefix[0] == '@')
            {
                var span = GetAkcssKeywordSpan(position);
                return CreateAkcssContext(
                    AkcssCompletionContextKind.BodyMember,
                    span,
                    owner,
                    declaration);
            }

            return CreateAkcssPropertyNameContext(
                position,
                owner,
                declaration);
        }

        if (TryGetAkcssModuleNameContext(
                line.Start,
                position,
                trimmedLinePrefix,
                out var moduleContext))
        {
            return moduleContext;
        }

        var isTopLevelKeyword =
            trimmedLinePrefix.Length == 0 ||
            trimmedLinePrefix[0] == '@';
        var topLevelSpan = isTopLevelKeyword
            ? GetAkcssKeywordSpan(position)
            : GetAkcssSelectorSnippetSpan(position);
        return new AkcssSyntacticCompletionContext(
            isTopLevelKeyword
                ? AkcssCompletionContextKind.TopLevel
                : AkcssCompletionContextKind.SelectorSnippet,
            topLevelSpan,
            Text.ToString(topLevelSpan));
    }

    private AkcssSyntacticCompletionContext
        CreateAkcssPropertyNameContext(
            int position,
            AkburaSyntax? owner,
            AkburaSyntax? declaration)
    {
        var fullSpan = GetAkcssPropertySpan(position);
        var fullName = Text.ToString(fullSpan);
        var lastDot = fullName.LastIndexOf('.');
        var qualifier = lastDot > 0
            ? fullName[..lastDot]
            : string.Empty;
        var applicableSpan = lastDot >= 0
            ? TextSpan.FromBounds(
                fullSpan.Start + lastDot + 1,
                fullSpan.End)
            : fullSpan;

        return CreateAkcssContext(
            AkcssCompletionContextKind.PropertyName,
            applicableSpan,
            owner,
            declaration,
            qualifier,
            fullName);
    }

    private bool TryGetAkcssAttachedPropertyExpressionContext(
        AkburaSyntax? declaration,
        AkburaSyntax? owner,
        int position,
        out AkcssSyntacticCompletionContext context)
    {
        context = default;
        if (declaration == null ||
            !TryGetEmbeddedCSharpContext(
                position,
                out var embeddedContext) ||
            embeddedContext.Kind !=
                AkburaCSharpCompletionContextKind.Expression)
        {
            return false;
        }

        var expressionText = Text.ToString(
            embeddedContext.HostSpan);
        var expression = CSharpSyntaxFactory.ParseExpression(
            expressionText);
        var relativePosition = embeddedContext.RelativePosition;
        var memberAccess = expression
            .DescendantNodesAndSelf()
            .OfType<CSharp.MemberAccessExpressionSyntax>()
            .Where(candidate =>
                candidate.Name is CSharp.IdentifierNameSyntax &&
                candidate.OperatorToken.Span.End <= relativePosition &&
                candidate.Name.Span.Start <= relativePosition &&
                relativePosition <= Math.Max(
                    candidate.Name.Span.End,
                    candidate.OperatorToken.Span.End))
            .OrderBy(static candidate => candidate.Name.Span.Length)
            .ThenByDescending(static candidate => candidate.Span.Start)
            .FirstOrDefault();
        if (memberAccess == null)
        {
            return false;
        }

        var qualifier = memberAccess.Expression.ToString().Trim();
        if (qualifier.Length == 0)
        {
            return false;
        }

        var nameStart = embeddedContext.HostSpan.Start +
            memberAccess.Name.Span.Start;
        if (nameStart > position)
        {
            return false;
        }

        var applicableSpan = TextSpan.FromBounds(
            nameStart,
            position);
        context = CreateAkcssContext(
            AkcssCompletionContextKind.AttachedPropertyExpression,
            applicableSpan,
            owner,
            declaration,
            qualifier);
        return true;
    }

    private AkcssSyntacticCompletionContext CreateAkcssContext(
        AkcssCompletionContextKind kind,
        TextSpan applicableSpan,
        AkburaSyntax? owner,
        AkburaSyntax? declaration,
        string? qualifier = null,
        string? propertyName = null)
    {
        return new AkcssSyntacticCompletionContext(
            kind,
            applicableSpan,
            Text.ToString(applicableSpan),
            owner?.Kind ?? SyntaxKind.None,
            owner?.FullSpan ?? default,
            declaration?.FullSpan ?? default,
            qualifier,
            propertyName);
    }

    private bool TryGetAkcssModuleNameContext(
        int lineStart,
        int position,
        string trimmedLinePrefix,
        out AkcssSyntacticCompletionContext context)
    {
        const string usingKeyword = "@using";
        if (!trimmedLinePrefix.StartsWith(
                usingKeyword,
                StringComparison.Ordinal))
        {
            context = default;
            return false;
        }

        var keywordOffset = Text.ToString(
                TextSpan.FromBounds(lineStart, position))
            .IndexOf(usingKeyword, StringComparison.Ordinal);
        var nameStart = lineStart + keywordOffset +
            usingKeyword.Length;
        while (nameStart < position &&
               char.IsWhiteSpace(Text[nameStart]))
        {
            nameStart++;
        }

        var span = TextSpan.FromBounds(nameStart, position);
        context = new AkcssSyntacticCompletionContext(
            AkcssCompletionContextKind.AkcssModuleName,
            span,
            Text.ToString(span));
        return true;
    }

    private AkburaSyntax? FindContainingAkcssDeclaration(
        AkburaSyntax root,
        int position)
    {
        return root.DescendantNodes()
            .Where(static node => node is
                AkcssStyleRuleSyntax or
                AkcssUtilityDeclarationSyntax)
            .Where(node => IsInsideAkcssBody(node, position))
            .OrderByDescending(static node => node.FullSpan.Start)
            .FirstOrDefault();
    }

    private AkburaSyntax? FindContainingAkcssBodyOwner(
        AkburaSyntax? declaration,
        int position)
    {
        if (declaration == null)
        {
            return null;
        }

        return declaration.DescendantNodes()
            .Where(static node => node is
                AkcssIfDirectiveSyntax or
                AkcssPseudoBlockSyntax)
            .Where(node => IsInsideAkcssBody(node, position))
            .OrderByDescending(static node => node.FullSpan.Start)
            .FirstOrDefault();
    }

    private AkcssApplyDirectiveSyntax? FindContainingAkcssApply(
        AkburaSyntax? declaration,
        int position)
    {
        return declaration?.DescendantNodes()
            .OfType<AkcssApplyDirectiveSyntax>()
            .Where(node =>
                node.FullSpan.Start <= position &&
                position <= Math.Max(
                    node.FullSpan.End,
                    node.ApplyKeyword.Span.End))
            .OrderByDescending(static node => node.FullSpan.Start)
            .FirstOrDefault();
    }

    private AkcssAssignmentSyntax? FindContainingAkcssAssignment(
        AkburaSyntax? declaration,
        int position)
    {
        return declaration?.DescendantNodes()
            .OfType<AkcssAssignmentSyntax>()
            .Where(node =>
                node.FullSpan.Start <= position &&
                position <= node.FullSpan.End)
            .OrderByDescending(static node => node.FullSpan.Start)
            .FirstOrDefault();
    }

    private bool IsInsideAkcssBody(
        AkburaSyntax node,
        int position)
    {
        SyntaxToken openBrace;
        SyntaxToken closeBrace;
        switch (node)
        {
            case AkcssStyleRuleSyntax style:
                openBrace = style.OpenBrace;
                closeBrace = style.CloseBrace;
                break;
            case AkcssUtilityDeclarationSyntax utility:
                openBrace = utility.OpenBrace;
                closeBrace = utility.CloseBrace;
                break;
            case AkcssIfDirectiveSyntax ifDirective:
                openBrace = ifDirective.OpenBrace;
                closeBrace = ifDirective.CloseBrace;
                break;
            case AkcssPseudoBlockSyntax pseudoBlock:
                openBrace = pseudoBlock.OpenBrace;
                closeBrace = pseudoBlock.CloseBrace;
                break;
            default:
                return false;
        }

        if (openBrace.IsMissing || position < openBrace.Span.End)
        {
            return false;
        }

        if (closeBrace.IsMissing)
        {
            return true;
        }

        var closeStart = Math.Max(
            openBrace.Span.End,
            node.Span.End - Math.Max(1, closeBrace.Span.Length));
        return position <= closeStart;
    }

    private static bool IsBeforeAkcssMemberEnd(
        SyntaxToken semicolon,
        AkburaSyntax member,
        int position)
    {
        return semicolon.IsMissing
            ? position <= member.FullSpan.End
            : position <= semicolon.Span.Start;
    }

    private TextSpan GetAkcssPropertySpan(int position)
    {
        var start = position;
        while (start > 0 &&
               IsAkcssPropertyCharacter(Text[start - 1]))
        {
            start--;
        }

        return TextSpan.FromBounds(start, position);
    }

    private TextSpan GetAkcssValueSpan(int position)
    {
        var start = position;
        while (start > 0 &&
               IsAkcssValueCharacter(Text[start - 1]))
        {
            start--;
        }

        return TextSpan.FromBounds(start, position);
    }

    private TextSpan GetAkcssKeywordSpan(int position)
    {
        var start = position;
        while (start > 0 &&
               (char.IsLetterOrDigit(Text[start - 1]) ||
                Text[start - 1] is '_' or '@'))
        {
            start--;
        }

        return TextSpan.FromBounds(start, position);
    }

    private TextSpan GetAkcssSelectorSnippetSpan(int position)
    {
        var start = position;
        while (start > 0 &&
               (char.IsLetterOrDigit(Text[start - 1]) ||
                Text[start - 1] is '_' or '-' or '.'))
        {
            start--;
        }

        return TextSpan.FromBounds(start, position);
    }

    private TextSpan GetTextualAkcssApplyItemSpan(
        int lineStart,
        int position)
    {
        var start = position;
        while (start > lineStart &&
               !char.IsWhiteSpace(Text[start - 1]) &&
               Text[start - 1] != ';')
        {
            start--;
        }

        return TextSpan.FromBounds(start, position);
    }

    private TextSpan ClampToPosition(
        TextSpan span,
        int position)
    {
        return TextSpan.FromBounds(
            Math.Min(span.Start, position),
            Math.Min(span.End, position));
    }

    private int FindAkcssAssignmentColon(
        int lineStart,
        int position)
    {
        for (var current = lineStart;
             current < position;
             current++)
        {
            if (Text[current] != ':' ||
                current > lineStart && Text[current - 1] == ':' ||
                current + 1 < position && Text[current + 1] == ':')
            {
                continue;
            }

            return current;
        }

        return -1;
    }

    private int GetFirstNonWhitespacePosition(
        int start,
        int end)
    {
        while (start < end && char.IsWhiteSpace(Text[start]))
        {
            start++;
        }

        return start;
    }

    private string GetTextualAkcssPropertyName(
        int lineStart,
        int colon)
    {
        var start = lineStart;
        for (var current = colon - 1;
             current >= lineStart;
             current--)
        {
            if (Text[current] is '{' or '}' or ';')
            {
                start = current + 1;
                break;
            }
        }

        start = GetFirstNonWhitespacePosition(start, colon);
        return Text.ToString(
                TextSpan.FromBounds(start, colon))
            .Trim();
    }

    private static bool IsAkcssPropertyCharacter(char value)
    {
        return char.IsLetterOrDigit(value) ||
            value is '_' or '.' or ':';
    }

    private static bool IsAkcssValueCharacter(char value)
    {
        return char.IsLetterOrDigit(value) ||
            value is '_' or '.' or '-' or '#';
    }
}
