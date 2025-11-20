using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.Syntax;

internal static class SyntaxNavigator
{
    private const int None = 0;

    private static readonly ObjectPool<Stack<ChildSyntaxList.Enumerator>> s_childEnumeratorStackPool =
        new(() => new Stack<ChildSyntaxList.Enumerator>(), 10);

    private static readonly ObjectPool<Stack<ChildSyntaxList.Reversed.Enumerator>> s_childReversedEnumeratorStackPool =
        new(() => new Stack<ChildSyntaxList.Reversed.Enumerator>(), 10);

    private static Func<SyntaxToken, bool> GetPredicateFunction(bool includeZeroWidth)
    {
        return includeZeroWidth ? SyntaxToken.Any : SyntaxToken.NonZeroWidth;
    }

    private static bool Matches(Func<SyntaxToken, bool>? predicate, SyntaxToken token)
    {
        return predicate == null || ReferenceEquals(predicate, SyntaxToken.Any) || predicate(token);
    }

    public static SyntaxToken GetFirstToken(in AkburaSyntax current, bool includeZeroWidth)
    {
        return GetFirstToken(current, GetPredicateFunction(includeZeroWidth));
    }

    public static SyntaxToken GetLastToken(in AkburaSyntax current, bool includeZeroWidth)
    {
        return GetLastToken(current, GetPredicateFunction(includeZeroWidth));
    }

    public static SyntaxToken GetPreviousToken(in SyntaxToken current, bool includeZeroWidth)
    {
        return GetPreviousToken(current, GetPredicateFunction(includeZeroWidth));
    }

    public static SyntaxToken GetNextToken(in SyntaxToken current, bool includeZeroWidth)
    {
        return GetNextToken(current, GetPredicateFunction(includeZeroWidth));
    }

    public static SyntaxToken GetFirstToken(AkburaSyntax current, Func<SyntaxToken, bool>? predicate)
    {
        var stack = s_childEnumeratorStackPool.Allocate();
        try
        {
            stack.Push(current.ChildNodesAndTokens().GetEnumerator());

            while (stack.Count > 0)
            {
                var en = stack.Pop();
                if (en.MoveNext())
                {
                    var child = en.Current;

                    if (child.IsToken)
                    {
                        var token = GetFirstToken(child.AsToken(), predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }

                    // push this enumerator back, not done yet
                    stack.Push(en);

                    if (child.IsNode)
                    {
                        Debug.Assert(child.IsNode);
                        stack.Push(child.AsNode()!.ChildNodesAndTokens().GetEnumerator());
                    }
                }
            }

            return default;
        }
        finally
        {
            stack.Clear();
            s_childEnumeratorStackPool.Free(stack);
        }
    }

    public static SyntaxToken GetLastToken(AkburaSyntax current, Func<SyntaxToken, bool> predicate)
    {
        var stack = s_childReversedEnumeratorStackPool.Allocate();
        try
        {
            stack.Push(current.ChildNodesAndTokens().Reverse().GetEnumerator());

            while (stack.Count > 0)
            {
                var en = stack.Pop();

                if (en.MoveNext())
                {
                    var child = en.Current;

                    if (child.IsToken)
                    {
                        var token = GetLastToken(child.AsToken(), predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }

                    // push this enumerator back, not done yet
                    stack.Push(en);

                    if (child.IsNode)
                    {
                        Debug.Assert(child.IsNode);
                        stack.Push(child.AsNode()!.ChildNodesAndTokens().Reverse().GetEnumerator());
                    }
                }
            }

            return default;
        }
        finally
        {
            stack.Clear();
            s_childReversedEnumeratorStackPool.Free(stack);
        }
    }

    private static SyntaxToken GetFirstToken(
        SyntaxToken token,
        Func<SyntaxToken, bool>? predicate)
    {
        if (Matches(predicate, token))
        {
            return token;
        }

        return default;
    }

    private static SyntaxToken GetLastToken(
        SyntaxToken token,
        Func<SyntaxToken, bool> predicate)
    {
        if (Matches(predicate, token))
        {
            return token;
        }

        return default;
    }

    public static SyntaxToken GetNextToken(
        AkburaSyntax node,
        Func<SyntaxToken, bool>? predicate)
    {
        while (node.Parent != null)
        {
            // walk forward in parent's child list until we find ourselves and then return the next token
            var returnNext = false;
            foreach (var child in node.Parent.ChildNodesAndTokens())
            {
                if (returnNext)
                {
                    if (child.IsToken)
                    {
                        var token = GetFirstToken(child.AsToken(), predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }
                    else
                    {
                        Debug.Assert(child.IsNode);
                        var token = GetFirstToken(child.AsNode()!, predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }
                }
                else if (child.IsNode && child.AsNode() == node)
                {
                    returnNext = true;
                }
            }

            // didn't find the next token in my parent's children, look up the tree
            node = node.Parent;
        }

        return default;
    }

    public static SyntaxToken GetPreviousToken(
        AkburaSyntax node,
        Func<SyntaxToken, bool> predicate)
    {
        while (node.Parent != null)
        {
            // walk backward in parent's child list until we find ourselves and then return the previous token
            var returnPrevious = false;
            foreach (var child in node.Parent.ChildNodesAndTokens().Reverse())
            {
                if (returnPrevious)
                {
                    if (child.IsToken)
                    {
                        var token = GetLastToken(child.AsToken(), predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }
                    else
                    {
                        Debug.Assert(child.IsNode);
                        var token = GetLastToken(child.AsNode()!, predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }
                }
                else if (child.IsNode && child.AsNode() == node)
                {
                    returnPrevious = true;
                }
            }

            // didn't find the previous token in my parent's children, look up the tree
            node = node.Parent;
        }

        return default;
    }

    public static SyntaxToken GetNextToken(
        in SyntaxToken current,
        Func<SyntaxToken, bool>? predicate)
    {
        if (current.Parent != null)
        {
            // walk forward in parent's child list until we find ourself 
            // and then return the next token
            var returnNext = false;
            foreach (var child in current.Parent.ChildNodesAndTokens())
            {
                if (returnNext)
                {
                    if (child.IsToken)
                    {
                        var token = GetFirstToken(child.AsToken(), predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }
                    else
                    {
                        Debug.Assert(child.IsNode);
                        var token = GetFirstToken(child.AsNode()!, predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }
                }
                else if (child.IsToken && child.AsToken() == current)
                {
                    returnNext = true;
                }
            }

            // otherwise get next token from the parent's parent, and so on
            return GetNextToken(current.Parent, predicate);
        }

        return default;
    }

    public static SyntaxToken GetPreviousToken(
        in SyntaxToken current,
        Func<SyntaxToken, bool> predicate)
    {
        if (current.Parent != null)
        {
            // walk backward in parent's child list until we find ourself 
            // and then return the previous token
            var returnPrevious = false;
            foreach (var child in current.Parent.ChildNodesAndTokens().Reverse())
            {
                if (returnPrevious)
                {
                    if (child.IsToken)
                    {
                        var token = GetLastToken(child.AsToken(), predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }
                    else
                    {
                        Debug.Assert(child.IsNode);
                        var token = GetLastToken(child.AsNode()!, predicate);
                        if (token.RawKind != None)
                        {
                            return token;
                        }
                    }
                }
                else if (child.IsToken && child.AsToken() == current)
                {
                    returnPrevious = true;
                }
            }

            // otherwise get previous token from the parent's parent, and so on
            return GetPreviousToken(current.Parent, predicate);
        }

        return default;
    }
}