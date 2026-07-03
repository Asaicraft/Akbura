using Akbura.Language.Syntax;
using System;
using System.Runtime.CompilerServices;

namespace Akbura.Language;

internal static class SemanticSyntaxIdentity
{
    public static bool Equals(AkburaSyntax? left, AkburaSyntax? right)
    {
        return ReferenceEquals(left, right);
    }

    public static bool IsInSameTree(AkburaSyntax left, AkburaSyntax right)
    {
        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        return ReferenceEquals(left.Root, right.Root);
    }

    public static int GetHashCode(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        return RuntimeHelpers.GetHashCode(syntax);
    }
}
