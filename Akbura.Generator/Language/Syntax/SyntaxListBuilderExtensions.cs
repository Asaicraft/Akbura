using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax;

internal static class SyntaxListBuilderExtensions
{
    public static SyntaxTokenList ToTokenList(this SyntaxListBuilder? builder)
    {
        if (builder == null || builder.Count == 0)
        {
            return default;
        }

        return new SyntaxTokenList(null, builder.ToListNode(), 0, 0);
    }

    public static SyntaxList<AkburaSyntax> ToList(this SyntaxListBuilder? builder)
    {
        var listNode = builder?.ToListNode();
        if (listNode is null)
        {
            return default;
        }

        return new SyntaxList<AkburaSyntax>(listNode.CreateRed());
    }

    public static SyntaxList<TNode> ToList<TNode>(this SyntaxListBuilder? builder)
        where TNode : AkburaSyntax
    {
        var listNode = builder?.ToListNode();
        if (listNode is null)
        {
            return default;
        }

        return new SyntaxList<TNode>(listNode.CreateRed());
    }

    public static SeparatedSyntaxList<TNode> ToSeparatedList<TNode>(this SyntaxListBuilder? builder) where TNode : AkburaSyntax
    {
        var listNode = builder?.ToListNode();
        if (listNode is null)
        {
            return default;
        }

        return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(listNode.CreateRed(), 0));
    }
}