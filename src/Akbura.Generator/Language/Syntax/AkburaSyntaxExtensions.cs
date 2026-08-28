using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura.Language.Syntax;
internal static partial class SyntaxNodeExtensions
{

    public static TNode AddAnnotations<TNode>(this TNode node, params AkburaSyntaxAnnotation[] annotations) where TNode : AkburaSyntax
    {
        return (TNode)node.Green.AddAnnotations(annotations).CreateRed();
    }

    public static TNode WithAnnotations<TNode>(this TNode node, params ImmutableArray<AkburaSyntaxAnnotation> annotations) where TNode : AkburaSyntax
    {
        return (TNode)node.Green.WithAnnotations(annotations).CreateRed();
    }

    public static TNode WithDiagnostics<TNode>(this TNode node, params ImmutableArray<AkburaDiagnostic> diagnostics) where TNode : AkburaSyntax
    {
        return (TNode)node.Green.WithDiagnostics(diagnostics).CreateRed();
    }
}