using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;
internal partial class GreenTailwindSegmentSyntax
{
    public override SyntaxToken CreateSeparator(AkburaSyntax element)
    {
        return SyntaxFactory.Token(SyntaxKind.MinusToken);
    }
}
