#if STATS
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    private IDisposable MeasureAkcssOperation(
        GenerationStatisticOperation operation,
        AkburaSyntax syntax,
        TextSpan span,
        string name,
        int itemCount)
    {
        var root = syntax.Root;
        // A foreign root is identified by reference, not by the caller's path.
        // Do not force declaration or semantic lookup just to produce a label.
        var sourcePath = ReferenceEquals(root, SyntaxTree.GetRootSyntax())
            ? SyntaxTree.FilePath
            : string.Empty;

        return GenerationStatistics.MeasureOperation(
            operation, this, root, sourcePath, span.Start, span.Length, name, itemCount);
    }
}
#endif
