using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;

namespace Akbura.Furioso;

internal sealed class AkcssGenerationSourceMap
{
    private readonly Dictionary<AkburaSyntax, AkburaSyntaxTree> _syntaxTreesByRoot = new();

    public AkcssGenerationSourceMap(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        foreach (var syntaxTree in syntaxTrees)
        {
            var root = syntaxTree.GetRootSyntax();
            if (!_syntaxTreesByRoot.ContainsKey(root))
            {
                _syntaxTreesByRoot.Add(root, syntaxTree);
            }
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

        path = syntaxTree switch
        {
            AkcssSyntaxTree { FilePath.Length: 0 } akcssTree => akcssTree.LogicalName,
            _ => syntaxTree.FilePath,
        };
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
