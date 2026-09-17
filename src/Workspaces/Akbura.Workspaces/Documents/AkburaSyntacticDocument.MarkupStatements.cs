using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Documents;

public sealed partial class AkburaSyntacticDocument
{
    private bool TryGetMarkupStatementContext(
        AkburaSyntax root,
        int position,
        out AkburaSyntacticCompletionContext context)
    {
        context = default;
        if (IsInsideComment(root, position))
        {
            return false;
        }

        var start = position;
        while (start > 0 && (char.IsLetter(Text[start - 1]) || Text[start - 1] is ' ' or '\t'))
        {
            start--;
        }

        var prefix = string.Empty;
        if (start > 0 && Text[start - 1] == '$')
        {
            start--;
            prefix = Text.ToString(TextSpan.FromBounds(start, position)).TrimEnd();
            if (!"$if".StartsWith(prefix, StringComparison.Ordinal) &&
                !"$foreach".StartsWith(prefix, StringComparison.Ordinal) &&
                !"$else if".StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
        }
        else
        {
            start = position;
            if (position > 0 && !char.IsWhiteSpace(Text[position - 1]) && Text[position - 1] is not ('>' or '}'))
            {
                return false;
            }
        }

        if (root.DescendantNodes().Any(node => node switch
            {
                MarkupStartTagSyntax tag => tag.Span.Start <= start &&
                    (tag.CloseToken.IsMissing ? start <= tag.Span.End : start < tag.CloseToken.Span.End),
                MarkupEndTagSyntax tag => tag.Span.Start <= start && start < tag.Span.End,
                _ => false,
            }) ||
            TryGetEmbeddedCSharpContext(position, out var embedded) && embedded.HostSpan.Length > 0)
        {
            return false;
        }

        AkburaSyntax? contentOwner = null;
        foreach (var node in root.DescendantNodes())
        {
            var containsPosition = node switch
            {
                MarkupElementSyntax { StartTag: { } tag } element =>
                    !tag.CloseToken.IsMissing && tag.CloseToken.Kind != SyntaxKind.SlashGreaterToken &&
                    tag.CloseToken.Span.End <= start &&
                    start <= (element.EndTag?.LessSlashToken.Span.Start ?? element.FullSpan.End),
                MarkupBlockSyntax block => !block.OpenBraceToken.IsMissing &&
                    block.OpenBraceToken.Span.End <= start &&
                    start <= (block.CloseBraceToken.IsMissing ? block.FullSpan.End : block.CloseBraceToken.Span.Start),
                MarkupCodeBlockSyntax block => !block.OpenBraceToken.IsMissing &&
                    block.OpenBraceToken.Span.End <= start &&
                    start <= (block.CloseBraceToken.IsMissing ? block.FullSpan.End : block.CloseBraceToken.Span.Start),
                _ => false,
            };
            if (containsPosition && (contentOwner == null || node.FullSpan.Length < contentOwner.FullSpan.Length))
            {
                contentOwner = node;
            }
        }

        if (contentOwner == null)
        {
            return false;
        }

        var canContinue = CanContinueMarkupConditional(root, contentOwner, start);
        context = new AkburaSyntacticCompletionContext(
            canContinue ? AkburaCompletionContextKind.MarkupConditionalContinuation :
                AkburaCompletionContextKind.MarkupStatement,
            TextSpan.FromBounds(start, position), prefix,
            componentName: null, parentComponentName: null, existingAttributeNames: []);
        return true;
    }

    private bool CanContinueMarkupConditional(AkburaSyntax root, AkburaSyntax owner, int position)
    {
        foreach (var statement in root.DescendantNodes().OfType<MarkupIfStatementSyntax>())
        {
            if (!ReferenceEquals(statement.Parent, owner) || statement.DollarToken.Span.Start >= position ||
                statement.ElseClause is { } finalElse && finalElse.DollarToken.Span.Start < position)
            {
                continue;
            }

            var body = statement.Body;
            foreach (var clause in statement.ElseIfClauses)
            {
                if (clause.DollarToken.Span.Start < position)
                {
                    body = clause.Body;
                }
            }

            if (!body.CloseBraceToken.IsMissing && body.CloseBraceToken.Span.End <= position &&
                ContainsOnlyStatementTrivia(body.CloseBraceToken.Span.End, position))
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsOnlyStatementTrivia(int start, int end)
    {
        if (ContainsOnlyWhitespace(Text, start, end))
        {
            return true;
        }

        var source = Text.ToString(TextSpan.FromBounds(start, end));
        var trivia = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseLeadingTrivia(source);
        return trivia.ToFullString() == source && trivia.All(item => item.RawKind is
            (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.WhitespaceTrivia or
            (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.EndOfLineTrivia or
            (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia or
            (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineCommentTrivia or
            (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineDocumentationCommentTrivia or
            (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineDocumentationCommentTrivia);
    }

    internal bool CanStartMarkupCondition(int position)
    {
        var root = SyntaxTree.GetRootSyntax();
        return TryGetMarkupStatementContext(root, position, out var context) &&
            (context.Prefix == "$if" || context.Prefix == "$else if" &&
                context.Kind == AkburaCompletionContextKind.MarkupConditionalContinuation);
    }
}
