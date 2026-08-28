using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax;

internal interface ICompilationUnitSyntax
{
    public SyntaxToken EndOfFileToken { get; }
}
