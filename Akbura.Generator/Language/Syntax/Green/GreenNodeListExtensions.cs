using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;
internal static class GreenNodeListExtensions
{
    public static GreenSyntaxList<T> ToGreenList<T>(this GreenNode? greenNode) where T: GreenNode
    {
        return greenNode == null 
            ? default
            : new GreenSyntaxList<T>(greenNode);
    }
}
