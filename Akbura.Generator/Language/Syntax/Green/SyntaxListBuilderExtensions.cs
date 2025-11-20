using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;

internal static class SyntaxListBuilderExtensions
{
    public static GreenSyntaxList<GreenNode> ToList(this GreenSyntaxListBuilder? builder)
    {
        return ToList<GreenNode>(builder);
    }

    public static GreenSyntaxList<TNode> ToList<TNode>(this GreenSyntaxListBuilder? builder) where TNode : GreenNode
    {
        if (builder == null)
        {
            return default(GreenSyntaxList<GreenNode>);
        }

        return new GreenSyntaxList<TNode>(builder.ToListNode());
    }
}