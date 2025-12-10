using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial class GreenNode
{
    [Flags]
    internal enum GreenNodeFlags : ushort
    {
        None = 0,
        ContainsDiagnostics = 1 << 3,
        ContainsAnnotations = 1 << 4,
        ContainsDiagnosticsDirectly = 1 << 5,
        ContainsAnnotationsDirectly = 1 << 6,
        IsNotMissing = 1 << 7,
        IsCSharpSyntax = 1 << 8,
        ContainsAkburaSyntaxInCSharpSyntax = 1 << 10,
        ContainsSkippedText = 1 << 11,

        InheritMask = ContainsDiagnostics | ContainsAnnotations | IsNotMissing | ContainsSkippedText,
    }
}
