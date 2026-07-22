using Akbura.Language;
using Akbura.Language.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Furioso;

internal sealed class AkcssGenerationSourceMap
{
    private readonly Dictionary<AkburaSyntax, AkcssSyntaxTree> _syntaxTreesByRoot = new();

    public AkcssGenerationSourceMap(ImmutableArray<AkcssSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var syntaxTree in syntaxTrees)
        {
            _syntaxTreesByRoot.Add(syntaxTree.GetRootSyntax(), syntaxTree);
        }
    }

    public bool TryGetLineDirective(
        AkburaSyntax syntax,
        out int lineNumber,
        out string path)
    {
        if (!_syntaxTreesByRoot.TryGetValue(syntax.Root, out var syntaxTree))
        {
            lineNumber = 0;
            path = string.Empty;
            return false;
        }

        path = string.IsNullOrWhiteSpace(syntaxTree.FilePath)
            ? syntaxTree.LogicalName
            : syntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(path) ||
            (uint)syntax.Position > (uint)syntaxTree.Text.Length)
        {
            lineNumber = 0;
            path = string.Empty;
            return false;
        }

        lineNumber = syntaxTree.Text.Lines.GetLineFromPosition(syntax.Position).LineNumber + 1;
        return true;
    }
}
