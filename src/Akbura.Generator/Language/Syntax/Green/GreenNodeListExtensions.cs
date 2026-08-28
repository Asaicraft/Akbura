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

    public static GreenSyntaxList<TGreen> ToGreenList<TGreen, TRed>(this SyntaxList<TRed> redList) 
        where TGreen : GreenNode 
        where TRed : AkburaSyntax
    {
        if (redList.Node == null)
        {
            return default;
        }

        return new GreenSyntaxList<TGreen>(redList.Node.Green);
    }

    public static SeparatedGreenSyntaxList<T> ToGreenSeparatedList<T>(this GreenNode? greenNode) where T: GreenNode
    {
        return greenNode == null
            ? new SeparatedGreenSyntaxList<T>(default)
            : new SeparatedGreenSyntaxList<T>(new GreenSyntaxList<GreenNode>(greenNode));
    }

    public static SeparatedGreenSyntaxList<TGreen> ToGreenSeparatedList<TGreen, TRed>(this SeparatedSyntaxList<TRed> greenNodeList) 
        where TGreen : GreenNode
        where TRed : AkburaSyntax
    {
        if (greenNodeList.Node == null)
        {
            return new SeparatedGreenSyntaxList<TGreen>(default);
        }

        return new SeparatedGreenSyntaxList<TGreen>(new GreenSyntaxList<GreenNode>(greenNodeList.Node.Green));
    }
}
