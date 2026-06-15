using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Akbura.Language.Operations;

internal readonly struct AkcssThicknessExpressionValue
{
    public AkcssThicknessExpressionValue(
        ExpressionSyntax left,
        ExpressionSyntax top,
        ExpressionSyntax right,
        ExpressionSyntax bottom)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Top = top ?? throw new ArgumentNullException(nameof(top));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        Bottom = bottom ?? throw new ArgumentNullException(nameof(bottom));
    }

    public ExpressionSyntax Left { get; }

    public ExpressionSyntax Top { get; }

    public ExpressionSyntax Right { get; }

    public ExpressionSyntax Bottom { get; }
}
