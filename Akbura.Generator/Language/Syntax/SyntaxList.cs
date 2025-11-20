using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax;

internal abstract partial class SyntaxList : AkburaSyntax
{
    public SyntaxList(GreenSyntaxList green, AkburaSyntax? parent, int position)
        : base(green, parent, position)
    {
    }
}