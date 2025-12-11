using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;

internal sealed class GreenSyntaxLastTokenReplacer : GreenSyntaxRewriter
{
    private readonly GreenSyntaxToken _oldToken;
    private readonly GreenSyntaxToken _newToken;

    private int _count = 1;
    private bool _found = false;

    private GreenSyntaxLastTokenReplacer(GreenSyntaxToken oldToken, GreenSyntaxToken newToken)
    {
        _oldToken = oldToken;
        _newToken = newToken;
    }

    internal static TRoot Replace<TRoot>(TRoot root, GreenSyntaxToken newToken)
        where TRoot : GreenNode
    {
        var oldToken = (GreenSyntaxToken)root.GetLastTerminal()!;
        var replacer = new GreenSyntaxLastTokenReplacer(oldToken, newToken);
        var newRoot = (TRoot)replacer.Visit(root)!;
        Debug.Assert(replacer._found);
        return newRoot;
    }

    private static int CountNonNullSlots(GreenNode node)
    {
        return node.ChildNodesAndTokens().Count;
    }

    public override GreenNode? Visit(GreenNode? node)
    {
        if (node != null && !_found)
        {
            _count--;
            if (_count == 0)
            {
                if (node is GreenSyntaxToken token)
                {
                    Debug.Assert(token == _oldToken);
                    _found = true;
                    return _newToken;
                }

                _count += CountNonNullSlots(node);
                return base.Visit(node);
            }
        }

        return node;
    }
}
