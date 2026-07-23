using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Furioso;

internal sealed class ComponentGenerationSourceMap
{
    private readonly ComponentSyntaxTree _syntaxTree;

    public ComponentGenerationSourceMap(ComponentSyntaxTree syntaxTree)
    {
        _syntaxTree = syntaxTree ?? throw new System.ArgumentNullException(nameof(syntaxTree));
    }

    public bool TryGetLineDirective(
        AkburaSyntax syntax,
        out LinePositionSpan lineSpan,
        out string path)
    {
        path = _syntaxTree.FilePath;
        var span = syntax.Span;
        if (!ReferenceEquals(syntax.Root, _syntaxTree.GetRootSyntax()) ||
            string.IsNullOrWhiteSpace(path) ||
            path.IndexOf('"') >= 0 ||
            path.IndexOf('\r') >= 0 ||
            path.IndexOf('\n') >= 0 ||
            span.Length == 0 ||
            (uint)span.Start > (uint)_syntaxTree.Text.Length ||
            (uint)span.End > (uint)_syntaxTree.Text.Length)
        {
            lineSpan = default;
            path = string.Empty;
            return false;
        }

        lineSpan = _syntaxTree.Text.Lines.GetLinePositionSpan(span);
        if (!IsValid(lineSpan.Start) ||
            !IsValid(lineSpan.End) ||
            lineSpan.End.Line == lineSpan.Start.Line &&
            lineSpan.End.Character <= lineSpan.Start.Character)
        {
            lineSpan = default;
            path = string.Empty;
            return false;
        }

        return true;
    }

    private static bool IsValid(LinePosition position)
    {
        return (uint)position.Line < 0x20000000 &&
               position.Line != 0xfeefee &&
               (uint)position.Character < 0x10000;
    }
}
