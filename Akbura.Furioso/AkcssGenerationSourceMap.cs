using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
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
        out LinePositionSpan lineSpan,
        out string path)
    {
        if (!_syntaxTreesByRoot.TryGetValue(syntax.Root, out var syntaxTree))
        {
            lineSpan = default;
            path = string.Empty;
            return false;
        }

        path = string.IsNullOrWhiteSpace(syntaxTree.FilePath)
            ? syntaxTree.LogicalName
            : syntaxTree.FilePath;
        var span = syntax.Span;
        if (string.IsNullOrWhiteSpace(path) ||
            path.IndexOf('"') >= 0 ||
            path.IndexOf('\r') >= 0 ||
            path.IndexOf('\n') >= 0 ||
            span.Length == 0 ||
            (uint)span.Start > (uint)syntaxTree.Text.Length ||
            (uint)span.End > (uint)syntaxTree.Text.Length)
        {
            lineSpan = default;
            path = string.Empty;
            return false;
        }

        lineSpan = syntaxTree.Text.Lines.GetLinePositionSpan(span);
        if (!IsValidLineSpan(lineSpan))
        {
            lineSpan = default;
            path = string.Empty;
            return false;
        }

        return true;
    }

    private static bool IsValidLineSpan(LinePositionSpan lineSpan)
    {
        return IsValidLinePosition(lineSpan.Start) &&
               IsValidLinePosition(lineSpan.End) &&
               (lineSpan.End.Line > lineSpan.Start.Line ||
                lineSpan.End.Character > lineSpan.Start.Character);
    }

    private static bool IsValidLinePosition(LinePosition position)
    {
        return (uint)position.Line < 0x20000000 &&
               position.Line != 0xfeefee &&
               (uint)position.Character < 0x10000;
    }
}
