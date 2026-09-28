using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura.Language.Syntax.Green;
internal partial class GreenSyntaxRewriter : GreenSyntaxVisitor<GreenNode>
{
    public override GreenNode DefaultVisit(GreenNode node)
    {
        return node;
    }

    [return: NotNullIfNotNull(nameof(token))]
    public override GreenNode? VisitToken(GreenSyntaxToken? token)
    {
        if (token == null)
        {
            return null;
        }

        var leading = this.VisitList(token.LeadingTrivia);
        var trailing = this.VisitList(token.TrailingTrivia);

        if (leading != token.LeadingTrivia || trailing != token.TrailingTrivia)
        {
            if (leading != token.LeadingTrivia)
            {
                token = token.TokenWithLeadingTrivia(leading.Node);
            }

            if (trailing != token.TrailingTrivia)
            {
                token = token.TokenWithTrailingTrivia(trailing.Node);
            }
        }

        return token;
    }

    public GreenSyntaxList<TNode> VisitList<TNode>(GreenSyntaxList<TNode> list) where TNode : GreenNode
    {
        GreenSyntaxListBuilder alternate = null!;
        for (int i = 0, n = list.Count; i < n; i++)
        {
            var item = list[i]!;
            var visited = this.Visit(item);
            if (item != visited && alternate == null)
            {
                alternate = new GreenSyntaxListBuilder(n);
                alternate.AddRange(list, 0, i);
            }

            if (alternate != null)
            {
                AkburaDebug.Assert(visited != null && visited.Kind != SyntaxKind.None, "Cannot remove node using Syntax.InternalSyntax.SyntaxRewriter.");
                alternate.Add(visited);
            }
        }

        if (alternate != null)
        {
            return alternate.ToList();
        }

        return list;
    }

    public SeparatedGreenSyntaxList<TNode> VisitList<TNode>(SeparatedGreenSyntaxList<TNode> list) where TNode : GreenNode
    {
        // A separated list contains syntax nodes and separator tokens in one
        // physical backing list. Visit that backing list so both kinds are
        // rewritten without casting separator tokens to the element type.
        var withSeps = list.GetWithSeparators();
        var result = this.VisitList(withSeps);
        if (result != withSeps)
        {
            return result.AsSeparatedList<TNode>();
        }

        return list;
    }
}
